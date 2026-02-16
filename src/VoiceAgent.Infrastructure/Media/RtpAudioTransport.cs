using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Audio;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Infrastructure.Media;

public sealed class RtpAudioTransport : IAudioTransport
{
    private readonly ILogger<RtpAudioTransport> _log;
    private readonly UdpClient _udp;

    private volatile bool _sendingEnabled = true;

    private readonly uint _ssrc;
    private ushort _seq;
    private uint _ts;

    private const int SampleRate = 8000;
    private const int Channels = 1;

    // 20ms @ 8kHz = 160 samples; PCMU uses 1 byte per sample
    private const int FrameBytes20ms = 160;
    private const byte MuLawSilence = 0xFF; // common "silence" byte for μ-law

    public IPEndPoint LocalEndpoint { get; }
    public IPEndPoint? RemoteEndpoint { get; private set; }

    public RtpAudioTransport(ILogger<RtpAudioTransport> log, int localPort)
    {
        _log = log;
        _udp = new UdpClient(localPort);

        LocalEndpoint = (IPEndPoint)_udp.Client.LocalEndPoint!;
        _ssrc = (uint)Random.Shared.NextInt64(1, int.MaxValue);
        _seq = (ushort)Random.Shared.Next(0, ushort.MaxValue);
        _ts = (uint)Random.Shared.NextInt64(0, int.MaxValue);

        _log.LogInformation("RTP listen {Endp}", LocalEndpoint);
    }

    // Stop sending NOW (barge-in). Auto-resume after small cooldown so future replies still work.
    public void StopSending()
    {
        _sendingEnabled = false;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(400); } catch { /* ignore */ }
            _sendingEnabled = true;
        });
    }

    public async IAsyncEnumerable<MuLawFrame> ReceiveAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await _udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { yield break; }

            RemoteEndpoint ??= res.RemoteEndPoint;

            var data = res.Buffer;
            if (data.Length < 12) continue;

            // RTP V2
            var version = data[0] >> 6;
            if (version != 2) continue;

            var cc = data[0] & 0x0F;
            var hdr = 12 + 4 * cc;
            if (data.Length <= hdr) continue;

            var payloadLen = data.Length - hdr;
            var payload = new byte[payloadLen];
            Buffer.BlockCopy(data, hdr, payload, 0, payloadLen);

            yield return new MuLawFrame(payload, SampleRate, Channels, Environment.TickCount64);
        }
    }

    public async ValueTask SendAsync(MuLawFrame frame, CancellationToken ct)
    {
        if (!_sendingEnabled) return;
        if (RemoteEndpoint is null) return;

        var src = frame.Data;
        var offset = 0;

        while (offset < src.Length && !ct.IsCancellationRequested)
        {
            if (!_sendingEnabled) return;

            var payload = new byte[FrameBytes20ms];
            Array.Fill(payload, MuLawSilence);

            var take = Math.Min(FrameBytes20ms, src.Length - offset);
            Array.Copy(src, offset, payload, 0, take);

            var pkt = new byte[12 + FrameBytes20ms];

            pkt[0] = 0x80; // RTP V2
            pkt[1] = 0x00; // PT=0 (PCMU)

            BinaryPrimitives.WriteUInt16BigEndian(pkt.AsSpan(2, 2), _seq++);
            BinaryPrimitives.WriteUInt32BigEndian(pkt.AsSpan(4, 4), _ts);
            BinaryPrimitives.WriteUInt32BigEndian(pkt.AsSpan(8, 4), _ssrc);

            Array.Copy(payload, 0, pkt, 12, FrameBytes20ms);

            _ts += FrameBytes20ms;

            await _udp.SendAsync(pkt, pkt.Length, RemoteEndpoint);

            offset += take;

            try
            {
                await Task.Delay(20, ct);
            }
            catch (OperationCanceledException ex)
            {
                _log.LogInformation(ex, "exception occurred");
                return;
            }
        }
    }


    public ValueTask DisposeAsync()
    {
        _udp.Dispose();
        return ValueTask.CompletedTask;
    }
}

using System.Net;
using VoiceAgent.Domain.Models.Audio;

namespace VoiceAgent.Domain.Ports;

public interface IAudioTransport : IAsyncDisposable
{
    IPEndPoint LocalEndpoint { get; }
    IPEndPoint? RemoteEndpoint { get; }
    IAsyncEnumerable<MuLawFrame> ReceiveAsync(CancellationToken ct);
    ValueTask SendAsync(MuLawFrame frame, CancellationToken ct);
    void StopSending();
}

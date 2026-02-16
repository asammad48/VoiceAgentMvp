using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VoiceAgent.Application.Orchestration;
using VoiceAgent.Domain.Ports;
using VoiceAgent.Infrastructure.Asterisk;

namespace VoiceAgent.Infrastructure.Telephony;

public sealed class AsteriskAriTelephonyControl : ITelephonyControl
{
    private readonly ILogger<AsteriskAriTelephonyControl> _log;
    private readonly AriClient _ari;
    private readonly Func<int, IAudioTransport> _audioFactory;
    private readonly Func<IAudioTransport, ConversationOrchestrator> _orchFactory;

    private readonly string _appName;
    private readonly string _windowsIp;
    private readonly int _port;

    private readonly string? _outboundEndpoint;
    private readonly string? _outboundCallerId;

    // ✅ Prevent double-processing and recursion loops
    private readonly ConcurrentDictionary<string, byte> _active = new();

    public AsteriskAriTelephonyControl(
        ILogger<AsteriskAriTelephonyControl> log,
        AriClient ari,
        Func<int, IAudioTransport> audioFactory,
        Func<IAudioTransport, ConversationOrchestrator> orchFactory,
        string appName,
        string windowsIp,
        int port,
        string? outboundEndpoint,
        string? outboundCallerId)
    {
        _log = log;
        _ari = ari;
        _audioFactory = audioFactory;
        _orchFactory = orchFactory;
        _appName = appName;
        _windowsIp = windowsIp;
        _port = port;
        _outboundEndpoint = outboundEndpoint;
        _outboundCallerId = outboundCallerId;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await _ari.ConnectEventsAsync(_appName, ct);

        if (!string.IsNullOrWhiteSpace(_outboundEndpoint))
        {
            _log.LogInformation("Originating outbound call to {Endpoint}", _outboundEndpoint);
            await _ari.OriginateAsync(_outboundEndpoint!, _appName, _outboundCallerId, ct);
        }

        await foreach (var ev in _ari.ReadEventsAsync(ct))
        {
            if (ev.Type != "StasisStart" || ev.Application != _appName || ev.Channel?.Id is null)
                continue;

            var id = ev.Channel.Id!;
            var name = ev.Channel.Name ?? "";
            var type = ev.Channel.Channeltype ?? "";

            if (name.Contains("UnicastRTP", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("ExternalMedia", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInformation("Ignoring media channel {Id}: {Name} ({Type})", id, name, type);
                continue;
            }

            // ✅ handle each SIP channel only once
            if (!_active.TryAdd(id, 1))
            {
                _log.LogInformation("Already handling channel {Id}", id);
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleCallAsync(id, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "HandleCallAsync failed for {ChannelId}", id);
                }
                finally
                {
                    _active.TryRemove(id, out _);
                }
            }, ct);
        }
    }

    private async Task HandleCallAsync(string channelId, CancellationToken ct)
    {
        _log.LogInformation("Handling call channel {ChannelId}", channelId);

        await _ari.AnswerAsync(channelId, ct);

        var bridgeId = await _ari.CreateBridgeAsync("mixing", ct);
        await _ari.AddChannelToBridgeAsync(bridgeId, channelId, ct);

        // ✅ IMPORTANT: start RTP listener BEFORE creating ExternalMedia
        await using var audio = _audioFactory(_port);

        var externalHost = $"{_windowsIp}:{_port}";
        _log.LogInformation("Creating ExternalMedia to {ExternalHost} format=ulaw", externalHost);

        var extChanId = await _ari.CreateExternalMediaAsync(_appName, externalHost, "ulaw", ct);
        await _ari.AddChannelToBridgeAsync(bridgeId, extChanId, ct);

        _log.LogInformation("Bridged SIP {SipChannel} + ExternalMedia {ExtChannel} on bridge {BridgeId}",
            channelId, extChanId, bridgeId);

        var orch = _orchFactory(audio);
        await orch.RunAsync(ct);
    }
}

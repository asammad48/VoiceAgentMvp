using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VoiceAgent.Application.Orchestration;
using VoiceAgent.Domain.Models.Api;
using VoiceAgent.Domain.Ports;
using VoiceAgent.Infrastructure.Asterisk;

namespace VoiceAgent.Infrastructure.Telephony;

public sealed class AsteriskAriTelephonyControl : ITelephonyControl
{
    private readonly ILogger<AsteriskAriTelephonyControl> _log;
    private readonly AriClient _ari;
    private readonly Func<int, IAudioTransport> _audioFactory;
    private readonly Func<IAudioTransport, ConversationOrchestrator> _orchFactory;
    private readonly IVoiceAgentApiClient _api;

    private readonly string _appName;
    private readonly string _windowsIp;
    private readonly int _port;

    private readonly string? _outboundEndpoint;
    private readonly string? _outboundCallerId;
    private readonly Guid _defaultTenantId;
    private readonly Guid _defaultAgentId;

    private readonly ConcurrentDictionary<string, byte> _active = new();
    private readonly ConcurrentDictionary<string, CallDto> _channelToCall = new();

    public AsteriskAriTelephonyControl(
        ILogger<AsteriskAriTelephonyControl> log,
        AriClient ari,
        Func<int, IAudioTransport> audioFactory,
        Func<IAudioTransport, ConversationOrchestrator> orchFactory,
        IVoiceAgentApiClient api,
        string appName,
        string windowsIp,
        int port,
        string? outboundEndpoint,
        string? outboundCallerId,
        Guid defaultTenantId,
        Guid defaultAgentId)
    {
        _log = log;
        _ari = ari;
        _audioFactory = audioFactory;
        _orchFactory = orchFactory;
        _api = api;
        _appName = appName;
        _windowsIp = windowsIp;
        _port = port;
        _outboundEndpoint = outboundEndpoint;
        _outboundCallerId = outboundCallerId;
        _defaultTenantId = defaultTenantId;
        _defaultAgentId = defaultAgentId;
    }

    public async Task TriggerOutboundAsync(CallDto call, CancellationToken ct)
    {
        var endpoint = "PJSIP/" + call.PhoneTo;
        _log.LogInformation("Originating outbound call {CallId} to {Endpoint}", call.Id, endpoint);
        var channelId = await _ari.OriginateAsync(endpoint, _appName, _outboundCallerId, ct);
        _channelToCall[channelId] = call;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await _ari.ConnectEventsAsync(_appName, ct);
        await foreach (var ev in _ari.ReadEventsAsync(ct))
        {
            if (ev.Type != "StasisStart" || ev.Application != _appName || ev.Channel?.Id is null) continue;
            var id = ev.Channel.Id!;
            if (ev.Channel.Name?.Contains("UnicastRTP") == true || ev.Channel.Channeltype?.Equals("ExternalMedia", StringComparison.OrdinalIgnoreCase) == true) continue;
            if (!_active.TryAdd(id, 1)) continue;

            _ = Task.Run(async () =>
            {
                string? bridgeId = null;
                string? extChanId = null;
                try
                {
                    CallDto? call = null;
                    if (!_channelToCall.TryRemove(id, out call))
                    {
                        var exten = ev.Channel.Dialplan?.Exten;
                        var useCase = exten switch { "2001" => "DOCTOR_APPT", "2002" => "CAB_BOOKING", _ => null };
                        if (useCase != null)
                        {
                            call = new CallDto(Guid.NewGuid(), _defaultTenantId, Guid.Empty, _defaultAgentId, "FE", useCase, "Inbound", ev.Channel.Name, "Inbound Caller", "Agent", null);
                        }
                    }

                    if (call != null)
                    {
                        await _ari.AnswerAsync(id, ct);
                        bridgeId = await _ari.CreateBridgeAsync("mixing", ct);
                        await _ari.AddChannelToBridgeAsync(bridgeId, id, ct);

                        await using var audio = _audioFactory(_port);
                        var externalHost = _windowsIp + ":" + _port;
                        extChanId = await _ari.CreateExternalMediaAsync(_appName, externalHost, "ulaw", ct);
                        await _ari.AddChannelToBridgeAsync(bridgeId, extChanId, ct);

                        var orch = _orchFactory(audio);
                        await orch.RunAsync(call, ct);
                    }
                    else
                    {
                        _log.LogWarning("No call metadata for {Id}, hanging up.", id);
                        await _ari.HangupAsync(id, ct);
                    }
                }
                catch (Exception ex) { _log.LogError(ex, "Error handling call {Id}", id); }
                finally
                {
                    if (bridgeId != null) await _ari.DeleteBridgeAsync(bridgeId, ct);
                    if (id != null) await _ari.HangupAsync(id, ct);
                    if (id != null) _active.TryRemove(id, out _);
                }
            }, ct);
        }
    }
}

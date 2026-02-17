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
    private readonly IVoiceAgentApiClient _api;
    private readonly Func<int, IAudioTransport> _audioFactory;
    private readonly Func<IAudioTransport, ConversationOrchestrator> _orchFactory;

    private readonly string _appName;
    private readonly string _windowsIp;
    private readonly int _port;

    private readonly string? _outboundEndpoint;
    private readonly string? _outboundCallerId;

    private readonly string _defaultCampaign;
    private readonly Guid? _defaultAgentId;
    private readonly Guid? _tenantId;
    private readonly Dictionary<string, string> _campaignByDid;

    // ✅ Prevent double-processing and recursion loops
    private readonly ConcurrentDictionary<string, byte> _active = new();

    public AsteriskAriTelephonyControl(
        ILogger<AsteriskAriTelephonyControl> log,
        AriClient ari,
        IVoiceAgentApiClient api,
        Func<int, IAudioTransport> audioFactory,
        Func<IAudioTransport, ConversationOrchestrator> orchFactory,
        string appName,
        string windowsIp,
        int port,
        string? outboundEndpoint,
        string? outboundCallerId,
        string defaultCampaign,
        Guid? defaultAgentId,
        Guid? tenantId,
        Dictionary<string, string> campaignByDid)
    {
        _log = log;
        _ari = ari;
        _api = api;
        _audioFactory = audioFactory;
        _orchFactory = orchFactory;
        _appName = appName;
        _windowsIp = windowsIp;
        _port = port;
        _outboundEndpoint = outboundEndpoint;
        _outboundCallerId = outboundCallerId;
        _defaultCampaign = defaultCampaign;
        _defaultAgentId = defaultAgentId;
        _tenantId = tenantId;
        _campaignByDid = campaignByDid;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await _ari.ConnectEventsAsync(_appName, ct);

        if (!string.IsNullOrWhiteSpace(_outboundEndpoint))
        {
            _log.LogInformation("Originating outbound call to {Endpoint}", _outboundEndpoint);
            await _ari.OriginateAsync(_outboundEndpoint!, _appName, _outboundCallerId, null, ct);
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
                    await HandleCallAsync(ev.Channel, ct);
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

    private async Task HandleCallAsync(AriChannel channel, CancellationToken ct)
    {
        var channelId = channel.Id!;
        _log.LogInformation("Handling call channel {ChannelId}", channelId);

        // Try to get variables from channel (outbound)
        var callIdStr = await _ari.GetVariableAsync(channelId, "CALL_ID", ct);
        var campaign = await _ari.GetVariableAsync(channelId, "CAMPAIGN", ct);

        Guid callId;
        if (Guid.TryParse(callIdStr, out var cid))
        {
            callId = cid;
            _log.LogInformation("Found CALL_ID={CallId} and CAMPAIGN={Campaign} from channel variables", callId, campaign);
        }
        else
        {
            // Inbound call - detect campaign from DID
            var exten = channel.Dialplan?.Exten ?? "s";
            var callerNumber = channel.Caller?.Number ?? "unknown";

            if (!_campaignByDid.TryGetValue(exten, out campaign))
            {
                campaign = _defaultCampaign;
            }

            _log.LogInformation("Inbound call detected: Exten={Exten}, Caller={Caller}. Mapping to campaign {Campaign}", exten, callerNumber, campaign);

            // Create call record
            callId = await _api.InboundStartAsync(campaign, callerNumber, _defaultAgentId, _tenantId, ct);
            _log.LogInformation("Created inbound call record: {CallId}", callId);
        }

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
        await orch.RunAsync(callId, campaign ?? _defaultCampaign, ct);
    }
}

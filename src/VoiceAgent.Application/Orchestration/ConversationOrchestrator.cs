using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Application.Orchestration;

public sealed class ConversationOrchestrator
{
    private readonly ILogger<ConversationOrchestrator> _log;
    private readonly IAudioTransport _audio;
    private readonly ISttProvider _stt;
    private readonly IVoiceAgentApiClient _api;
    private readonly ITtsProvider _tts;
    private readonly IVadDetector _vad;

    private readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(12);

    public ConversationOrchestrator(
        ILogger<ConversationOrchestrator> log,
        IAudioTransport audio,
        ISttProvider stt,
        IVoiceAgentApiClient api,
        ITtsProvider tts,
        IVadDetector vad)
    {
        _log = log;
        _audio = audio;
        _stt = stt;
        _api = api;
        _tts = tts;
        _vad = vad;
    }

    public async Task RunAsync(Guid callId, string campaign, CancellationToken ct)
    {
        _log.LogInformation("Starting conversation orchestration for Call {CallId}, Campaign {Campaign}", callId, campaign);

        await _stt.StartAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pumpTask = PumpAudioToSttAsync(cts.Token);
        var logicTask = HandleConversationAsync(callId, campaign, cts.Token);

        await Task.WhenAny(pumpTask, logicTask);
        cts.Cancel();
        _log.LogInformation("Conversation orchestration ended for Call {CallId}", callId);
    }

    private async Task PumpAudioToSttAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _audio.ReceiveAsync(ct))
            {
                await _stt.SendAudioAsync(frame, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "PumpAudioToSttAsync failed");
        }
    }

    private async Task HandleConversationAsync(Guid callId, string campaign, CancellationToken ct)
    {
        var action = await _api.GetIntroAsync(callId, ct);

        CancellationTokenSource? speakCts = null;
        bool isSpeaking = false;
        var lastActivity = DateTimeOffset.UtcNow;
        var reprompted = false;

        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        async Task StartSpeaking(AgentAction act) {
            speakCts?.Cancel();
            speakCts = CancellationTokenSource.CreateLinkedTokenSource(loopCts.Token);
            isSpeaking = true;
            _log.LogInformation("Agent: {Text} [Intent: {Intent}]", act.Say, act.Intent);

            try {
                if (!string.IsNullOrWhiteSpace(act.Say)) {
                    await foreach (var frame in _tts.SynthesizeMuLawAsync(act.Say, speakCts.Token))
                        await _audio.SendAsync(frame, speakCts.Token);
                }
            } catch (OperationCanceledException) {
                _log.LogInformation("Agent speech cancelled (barge-in or new action).");
                _audio.StopSending();
            } catch (Exception ex) {
                _log.LogError(ex, "Error during agent speech.");
            } finally {
                isSpeaking = false;
            }

            // Handle intents that end the call or trigger special logic
            if (act.Intent == "set_callback") {
                await _api.UpdateStatusAsync(callId, CallStatus.CallbackScheduled, "Scheduled", false, loopCts.Token);
            } else if (act.Intent == "transfer") {
                _log.LogInformation("Triggering TransferToHuman for Call {CallId}", callId);
                await _api.UpdateStatusAsync(callId, CallStatus.Transferred, "Transfer to human", true, loopCts.Token);
                loopCts.Cancel();
            } else if (act.Intent == "dncl" || act.Intent == "end") {
                loopCts.Cancel();
            }
        }

        // Start with intro
        _ = StartSpeaking(action);

        // Timeout task
        _ = Task.Run(async () => {
            try {
                while (!loopCts.Token.IsCancellationRequested) {
                    await Task.Delay(1000, loopCts.Token);
                    if (DateTimeOffset.UtcNow - lastActivity > _callTimeout) {
                        if (!reprompted) {
                            _log.LogInformation("No speech detected. Reprompting...");
                            reprompted = true;
                            lastActivity = DateTimeOffset.UtcNow;
                            _ = StartSpeaking(new AgentAction { Say = "Are you still there?", Intent = "reprompt" });
                        } else {
                            _log.LogInformation("No speech detected after reprompt. Ending call.");
                            await _api.UpdateStatusAsync(callId, CallStatus.NoAnswer, "No response", true, loopCts.Token);
                            loopCts.Cancel();
                        }
                    }
                }
            } catch (OperationCanceledException) { }
        }, loopCts.Token);

        try {
            await foreach (var upd in _stt.GetUpdatesAsync(loopCts.Token))
            {
                if (!string.IsNullOrWhiteSpace(upd.Text)) {
                    lastActivity = DateTimeOffset.UtcNow;
                    if (isSpeaking && !upd.IsFinal) {
                        speakCts?.Cancel();
                    }
                }

                if (upd.IsFinal && !string.IsNullOrWhiteSpace(upd.Text)) {
                    var text = upd.Text.Trim();
                    _log.LogInformation("User: {Text}", text);
                    reprompted = false;

                    // Local fast-rules
                    if (IsDnc(text)) {
                        _log.LogInformation("Local DNC detected.");
                        await _api.UpdateStatusAsync(callId, CallStatus.Dnc, "Local DNC", true, loopCts.Token);
                        break;
                    }
                    if (IsNotInterested(text)) {
                        _log.LogInformation("Local NotInterested detected.");
                        await _api.UpdateStatusAsync(callId, CallStatus.NotInterested, "Local NotInterested", true, loopCts.Token);
                        break;
                    }

                    action = await _api.GetNextActionAsync(callId, text, null, loopCts.Token);
                    _ = StartSpeaking(action);
                }
            }
        } catch (OperationCanceledException) { }
        finally {
            loopCts.Cancel();
            speakCts?.Cancel();
            _audio.StopSending();
        }
    }

    private bool IsDnc(string text)
    {
        var t = text.ToLowerInvariant();
        return t.Contains("do not call") || t.Contains("remove me") || t.Contains("stop calling") || t.Contains("remove from your list");
    }

    private bool IsNotInterested(string text)
    {
        var t = text.ToLowerInvariant();
        return t.Contains("not interested") || t.Contains("don't want") || t.Contains("no thank") || t.Contains("not looking for");
    }
}

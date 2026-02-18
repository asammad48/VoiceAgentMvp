using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Api;
using VoiceAgent.Domain.Models.Audio;
using VoiceAgent.Domain.Ports;
using System.Diagnostics;

namespace VoiceAgent.Application.Orchestration;

public sealed class ConversationOrchestrator
{
    private readonly ILogger<ConversationOrchestrator> _log;
    private readonly IAudioTransport _audio;
    private readonly ISttProvider _stt;
    private readonly ITtsProvider _tts;
    private readonly IVadDetector _vad;
    private readonly IVoiceAgentApiClient _api;

    private readonly TimeSpan _silenceToFinalize = TimeSpan.FromMilliseconds(900);
    private readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _introBargeInGrace = TimeSpan.FromMilliseconds(300);
    private readonly TimeSpan _maxCallDuration = TimeSpan.FromMinutes(10);

    public ConversationOrchestrator(
        ILogger<ConversationOrchestrator> log,
        IAudioTransport audio,
        ISttProvider stt,
        ITtsProvider tts,
        IVadDetector vad,
        IVoiceAgentApiClient api)
    {
        _log = log;
        _audio = audio;
        _stt = stt;
        _tts = tts;
        _vad = vad;
        _api = api;
    }

    public async Task RunAsync(CallDto call, CancellationToken ct)
    {
        _log.LogInformation("Starting hardened conversation for call {CallId} (Campaign: {Campaign})", call.Id, call.CampaignCode);

        using var callTimeoutCts = new CancellationTokenSource(_maxCallDuration);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, callTimeoutCts.Token);
        var token = linkedCts.Token;

        try
        {
            await _stt.StartAsync(token);

            var pumpTask = PumpAudioToSttAsync(token);


            // Initial intro pitch
            if (!string.IsNullOrWhiteSpace(call.IntroPitch))
            {
                var intro = call.IntroPitch.Replace("{lead_name}", call.LeadName).Replace("{agent_name}", call.AgentName);
                await PlayIntroAsync(intro, token);
            }

            var loopTask = HandleTranscriptsAsync(call, token);

            await Task.WhenAll(pumpTask, loopTask);
        }
        catch (OperationCanceledException) when (callTimeoutCts.IsCancellationRequested)
        {
            _log.LogWarning("Max call duration reached for {CallId}", call.Id);
            await _api.UpdateStatusAsync(call.TenantId, call.Id, CallStatusDto.Completed, "Max duration reached", true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error in conversation {CallId}", call.Id);
            await _api.UpdateStatusAsync(call.TenantId, call.Id, CallStatusDto.Failed, ex.Message, true, CancellationToken.None);
        }
        finally
        {
            _audio.StopSending();
            await _api.UpdateStatusAsync(call.TenantId, call.Id, CallStatusDto.Completed, "Finalized", true, CancellationToken.None);
            _log.LogInformation("Hardened conversation finalized for call {CallId}", call.Id);
        }
    }

    private async Task PumpAudioToSttAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _audio.ReceiveAsync(ct))
            {
                _vad.IsSpeech(frame);
                await _stt.SendAudioAsync(frame, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task PlayIntroAsync(string intro, CancellationToken ct)
    {
        _log.LogInformation("Playing intro: {Intro}", intro);

        using var introCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ttsTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var outFrame in _tts.SynthesizeMuLawAsync(intro, introCts.Token))
                {
                    await _audio.SendAsync(outFrame, introCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "TTS failure during intro");
            }
        }, ct);

        var start = DateTimeOffset.UtcNow;
        try
        {
            await foreach (var upd in _stt.GetUpdatesAsync(introCts.Token))
            {
                if (ttsTask.IsCompleted) break;

                bool bargeIn = !string.IsNullOrWhiteSpace(upd.Text) || _vad.IsSpeech(new MuLawFrame(new byte[0], 8000, 1, 0));
                if (bargeIn && (DateTimeOffset.UtcNow - start > _introBargeInGrace))
                {
                    _log.LogInformation("Barge-in detected during intro (STT or VAD).");
                    _audio.StopSending();
                    introCts.Cancel();
                    break;
                }
            }
            await ttsTask;
        }
        catch (OperationCanceledException) { }
    }

    private async Task HandleTranscriptsAsync(CallDto call, CancellationToken ct)
    {
        var lastActivityAt = DateTimeOffset.UtcNow;
        var fields = new Dictionary<string, string>();
        CancellationTokenSource? speakCts = null;
        bool isProcessing = false;

        try
        {
            await foreach (var upd in _stt.GetUpdatesAsync(ct))
            {
                if (!string.IsNullOrWhiteSpace(upd.Text))
                {
                    lastActivityAt = DateTimeOffset.UtcNow;
                    if (speakCts != null && !speakCts.IsCancellationRequested)
                    {
                        _log.LogInformation("Barge-in: stopping agent speech.");
                        _audio.StopSending();
                        speakCts.Cancel();
                    }
                }
                _log.LogInformation("[TURN DEBUG] STT Update: [Final={IsFinal}] Text: {Text}", upd.IsFinal, upd.Text);
                if (upd.IsFinal && !string.IsNullOrWhiteSpace(upd.Text))
                {
                    isProcessing = true;
                    try
                    {
                        var userText = upd.Text.Trim();
                        var sw = Stopwatch.StartNew();
                        var action = await _api.GetNextActionAsync(call.TenantId, call.Id, userText, fields, ct);
                        sw.Stop();
                        _log.LogInformation("LLM Latency: {Ms}ms", sw.ElapsedMilliseconds);

                        if (action.Fields != null)
                        {
                            foreach (var kv in action.Fields) fields[kv.Key] = kv.Value;
                        }

                        if (!string.IsNullOrWhiteSpace(action.Say))
                        {
                            speakCts?.Dispose();
                            speakCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            var localToken = speakCts.Token;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await foreach (var outFrame in _tts.SynthesizeMuLawAsync(action.Say, localToken))
                                    {
                                        await _audio.SendAsync(outFrame, localToken);
                                        lastActivityAt = DateTimeOffset.UtcNow;
                                    }
                                }
                                catch (OperationCanceledException) { }
                                catch (Exception ex)
                                {
                                    _log.LogError(ex, "TTS Provider failure");
                                }
                            }, ct);
                        }

                        if (action.Intent?.ToLowerInvariant() == "end" || action.Intent?.ToLowerInvariant() == "dncl")
                        {
                            await Task.Delay(2000, ct);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "LLM Provider failure");
                        await foreach (var f in _tts.SynthesizeMuLawAsync("I am sorry, I am having trouble connecting. Let me call you back.", ct))
                        {
                            await _audio.SendAsync(f, ct);
                        }
                        break;
                    }
                    finally
                    {
                        isProcessing = false;
                        lastActivityAt = DateTimeOffset.UtcNow;
                    }
                }

                if (!isProcessing && (DateTimeOffset.UtcNow - lastActivityAt > _callTimeout))
                {
                    _log.LogInformation("Silence timeout reached.");
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            speakCts?.Dispose();
        }
    }
}

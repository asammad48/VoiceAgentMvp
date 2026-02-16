using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Application.Orchestration;

public sealed class ConversationOrchestrator
{
    private readonly ILogger<ConversationOrchestrator> _log;
    private readonly IAudioTransport _audio;
    private readonly ISttProvider _stt;
    private readonly ILlmProvider _llm;
    private readonly ITtsProvider _tts;
    private readonly IVadDetector _vad;

    private readonly TimeSpan _silenceToFinalize = TimeSpan.FromMilliseconds(900);
    private readonly TimeSpan _minListenAfterBargeIn = TimeSpan.FromMilliseconds(700);

    public ConversationOrchestrator(
        ILogger<ConversationOrchestrator> log,
        IAudioTransport audio,
        ISttProvider stt,
        ILlmProvider llm,
        ITtsProvider tts,
        IVadDetector vad)
    {
        _log = log;
        _audio = audio;
        _stt = stt;
        _llm = llm;
        _tts = tts;
        _vad = vad;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var turns = new List<ChatTurn>
        {
            new(ChatRole.System, "You are a concise phone-call voice agent. Keep replies short and clear.")
        };

        await _stt.StartAsync(ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = PumpAudioToSttAsync(linked.Token);
        var loop = HandleTranscriptsAsync(turns, linked.Token);
        await Task.WhenAll(pump, loop);
    }

    private async Task PumpAudioToSttAsync(CancellationToken ct)
    {
        await foreach (var frame in _audio.ReceiveAsync(ct))
        {
            _ = _vad.IsSpeech(frame);
            await _stt.SendAudioAsync(frame, ct);
        }
    }

    private async Task HandleTranscriptsAsync(List<ChatTurn> turns, CancellationToken ct)
    {
        string lastFinal = "";
        var lastFinalAt = DateTimeOffset.MinValue;

        CancellationTokenSource? speakCts = null;
        var speaking = false;
        var lastBargeInAt = DateTimeOffset.MinValue;


        Task? finalizeTask = null;
        CancellationTokenSource? finalizeCts = null;

        await foreach (var upd in _stt.GetUpdatesAsync(ct))
        {
            if (upd.IsFinal && !string.IsNullOrWhiteSpace(upd.Text))
            {
                lastFinal = upd.Text.Trim();
                _log.LogInformation("STT final: {Text}", lastFinal);

                // reset finalize timer
                finalizeCts?.Cancel();
                finalizeCts?.Dispose();
                finalizeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                var localToken = finalizeCts.Token;
                finalizeTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_silenceToFinalize, localToken);
                        if (localToken.IsCancellationRequested) return;

                        // finalize here
                        var userText = lastFinal;
                        lastFinal = "";

                        turns.Add(new(ChatRole.User, userText));
                        var reply = (await _llm.CompleteAsync(turns, ct)).Trim();
                        if (reply.Length == 0) reply = "Sorry, could you repeat that?";
                        turns.Add(new(ChatRole.Assistant, reply));

                        await foreach (var outFrame in _tts.SynthesizeMuLawAsync(reply, ct))
                            await _audio.SendAsync(outFrame, ct);
                    }
                    catch (OperationCanceledException) { }
                }, ct);
            }
        }

        //await foreach (var upd in _stt.GetUpdatesAsync(ct))
        //{
        //    Console.WriteLine($"Handle transcript {upd.Text}");
        //    if (speaking && !upd.IsFinal && !string.IsNullOrWhiteSpace(upd.Text))
        //    {
        //        _log.LogInformation("BARGE-IN detected. Stopping TTS.");
        //        _audio.StopSending();
        //        speakCts?.Cancel();
        //        speaking = false;
        //        lastBargeInAt = DateTimeOffset.UtcNow;
        //        lastFinal = "";
        //        continue;
        //    }
        //    Console.WriteLine($"loop passed");
        //    if (upd.IsFinal && !string.IsNullOrWhiteSpace(upd.Text))
        //    {
        //        lastFinal = upd.Text.Trim();
        //        lastFinalAt = DateTimeOffset.UtcNow;
        //        _log.LogInformation("STT final: {Text}", lastFinal);
        //    }
        //    Console.WriteLine($"Handle transcript {upd.Text}");
        //    if (!string.IsNullOrEmpty(lastFinal) && lastFinalAt != DateTimeOffset.MinValue)
        //    {
        //        Console.WriteLine($"in if Handle transcript {upd.Text}");
        //        if (DateTimeOffset.UtcNow - lastFinalAt >= _silenceToFinalize)
        //        {
        //            Console.WriteLine($"in if if Handle transcript {upd.Text}");
        //            if (DateTimeOffset.UtcNow - lastBargeInAt < _minListenAfterBargeIn)
        //                continue;
        //            Console.WriteLine($"passed if Handle transcript {upd.Text}");
        //            var userText = lastFinal;
        //            lastFinal = "";

        //            turns.Add(new(ChatRole.User, userText));
        //            var reply = (await _llm.CompleteAsync(turns, ct)).Trim();
        //            if (reply.Length == 0) reply = "Sorry, could you repeat that?";
        //            turns.Add(new(ChatRole.Assistant, reply));

        //            speakCts?.Dispose();
        //            speakCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        //            speaking = true;

        //            try
        //            {
        //                Console.WriteLine(reply);
        //                await foreach (var outFrame in _tts.SynthesizeMuLawAsync(reply, speakCts.Token))
        //                    await _audio.SendAsync(outFrame, speakCts.Token);
        //            }
        //            catch (OperationCanceledException)
        //            {
        //                _log.LogInformation("TTS cancelled.");
        //            }
        //            finally
        //            {
        //                speaking = false;
        //            }
        //        }
        //    }
        //}
    }
}

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Audio;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Infrastructure.Providers.Deepgram;

public sealed class DeepgramSttProvider : ISttProvider
{
    private readonly ILogger<DeepgramSttProvider> _log;
    private readonly string _apiKey;
    private readonly Uri _wsUri;
    private ClientWebSocket? _ws;
    private readonly Channel<TranscriptUpdate> _updates = Channel.CreateUnbounded<TranscriptUpdate>();

    public DeepgramSttProvider(ILogger<DeepgramSttProvider> log, string apiKey, Uri wsUri)
    {
        _log = log;
        _apiKey = apiKey;
        _wsUri = wsUri;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {_apiKey}");
        await _ws.ConnectAsync(_wsUri, ct);
        _ = Task.Run(() => ReadLoopAsync(ct), ct);
    }

    public async ValueTask SendAudioAsync(MuLawFrame frame, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open) return;
        await _ws.SendAsync(frame.Data, WebSocketMessageType.Binary, true, ct);
    }

    public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public async IAsyncEnumerable<TranscriptUpdate> GetUpdatesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _updates.Reader.WaitToReadAsync(ct))
            while (_updates.Reader.TryRead(out var item))
                yield return item;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_ws is null) return;
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult res;
            try { res = await _ws.ReceiveAsync(buffer, ct); }
            catch (Exception ex) { Console.WriteLine(ex); break; }

            if (res.MessageType == WebSocketMessageType.Close) break;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
            if (!res.EndOfMessage) continue;

            var json = sb.ToString();
            sb.Clear();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool isFinal = root.TryGetProperty("is_final", out var fin) && fin.GetBoolean();
                string text = "";

                if (root.TryGetProperty("channel", out var channel) &&
                    channel.TryGetProperty("alternatives", out var alts) &&
                    alts.ValueKind == JsonValueKind.Array &&
                    alts.GetArrayLength() > 0)
                {
                    var alt0 = alts[0];
                    if (alt0.TryGetProperty("transcript", out var tr))
                        text = tr.GetString() ?? "";
                }

                if (!string.IsNullOrWhiteSpace(text))
                    await _updates.Writer.WriteAsync(new TranscriptUpdate(text, isFinal), ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Deepgram parse issue");
            }
        }

        _updates.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws is not null && _ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch { }
        _ws?.Dispose();
    }
}

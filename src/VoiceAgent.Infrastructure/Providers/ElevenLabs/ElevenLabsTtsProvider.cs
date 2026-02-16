using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Audio;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Infrastructure.Providers.ElevenLabs;

public sealed class ElevenLabsTtsProvider : ITtsProvider
{
    private readonly ILogger<ElevenLabsTtsProvider> _log;
    private readonly HttpClient _http;
    private readonly string _voiceId;
    private readonly string _modelId;

    public ElevenLabsTtsProvider(ILogger<ElevenLabsTtsProvider> log, HttpClient http, string apiKey, string voiceId, string modelId)
    {
        _log = log;
        _http = http;
        _voiceId = voiceId;
        _modelId = modelId;

        _http.DefaultRequestHeaders.Remove("xi-api-key");
        _http.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mulaw"));
    }

    public async IAsyncEnumerable<MuLawFrame> SynthesizeMuLawAsync(string text, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{_voiceId}/stream?output_format=ulaw_8000&optimize_streaming_latency=3";

        var payload = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["model_id"] = _modelId,
            ["voice_settings"] = new Dictionary<string, object?>
            {
                ["stability"] = 0.4,
                ["similarity_boost"] = 0.7
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _log.LogWarning("ElevenLabs TTS error {Status}: {Body}", resp.StatusCode, err);
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[4096];
        var carry = new List<byte>(8192);

        while (true)
        {
            int n = await stream.ReadAsync(buf, 0, buf.Length, ct);
            if (n <= 0) break;
            carry.AddRange(buf.Take(n));

            while (carry.Count >= 160)
            {
                var frame = carry.GetRange(0, 160).ToArray();
                carry.RemoveRange(0, 160);
                yield return new MuLawFrame(frame, 8000, 1, Environment.TickCount64);
            }
        }

        if (carry.Count > 0)
        {
            var frame = new byte[160];
            Array.Fill(frame, (byte)0xFF);
            carry.CopyTo(0, frame, 0, Math.Min(carry.Count, 160));
            yield return new MuLawFrame(frame, 8000, 1, Environment.TickCount64);
        }
    }
}

using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace VoiceAgent.Infrastructure.Providers.Deepgram;

public sealed class DeepgramPrerecordedStt
{
    private readonly ILogger<DeepgramPrerecordedStt> _log;
    private readonly HttpClient _http;

    public DeepgramPrerecordedStt(ILogger<DeepgramPrerecordedStt> log, HttpClient http, string apiKey)
    {
        _log = log;
        _http = http;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", apiKey);
    }

    public async Task<string> TranscribeAsync(Stream audio, string contentType, string queryString, CancellationToken ct)
    {
        var url = $"https://api.deepgram.com/v1/listen?{queryString}".TrimEnd('?');
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        var content = new StreamContent(audio);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        req.Content = content;

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Deepgram prerecorded STT error {Status}: {Body}", resp.StatusCode, body);
            return "";
        }
        return body;
    }
}

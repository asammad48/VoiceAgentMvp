using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Infrastructure.Providers.Llm;

public sealed class HfRouterLlmProvider : ILlmProvider
{
    private readonly ILogger<HfRouterLlmProvider> _log;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;

    public HfRouterLlmProvider(ILogger<HfRouterLlmProvider> log, HttpClient http, Uri endpoint, string hfToken, string model)
    {
        _log = log;
        _http = http;
        _endpoint = endpoint;
        _model = model;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hfToken);
    }

    public async Task<string> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        var msgs = turns.Select(t => new Dictionary<string, string>
        {
            ["role"] = t.Role switch { ChatRole.System => "system", ChatRole.Assistant => "assistant", _ => "user" },
            ["content"] = t.Content
        }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["stream"] = false,
            ["temperature"] = 0.3,
            ["messages"] = msgs
        };

        var json = JsonSerializer.Serialize(payload);
        using var resp = await _http.PostAsync(_endpoint, new StringContent(json, Encoding.UTF8, "application/json"), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("HF router error {Status}: {Body}", resp.StatusCode, body);
            return "Sorry, I'm having trouble right now.";
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}

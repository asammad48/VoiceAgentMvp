using System.Net.Http.Json;
using VoiceAgent.Domain.Models.Api;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Infrastructure.Telephony;

public sealed class VoiceAgentApiClient : IVoiceAgentApiClient
{
    private readonly HttpClient _http;

    public VoiceAgentApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<CallDto?> ClaimNextCallAsync(Guid tenantId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/calls/claim");
        req.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict) return null; // DNC blocked

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<CallDto>(cancellationToken: ct);
    }

    public async Task<AgentActionDto> GetNextActionAsync(Guid tenantId, Guid callId, string transcript, Dictionary<string, string>? fields, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/calls/" + callId + "/next");
        req.Headers.Add("X-Tenant-Id", tenantId.ToString());
        req.Content = JsonContent.Create(new { Transcript = transcript, Fields = fields });

        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadFromJsonAsync<AgentActionDto>(cancellationToken: ct) ?? new AgentActionDto("Error", "transfer", null, "error");
    }

    public async Task UpdateStatusAsync(Guid tenantId, Guid callId, CallStatusDto status, string? notes = null, bool endCall = false, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/calls/" + callId + "/status");
        req.Headers.Add("X-Tenant-Id", tenantId.ToString());
        req.Content = JsonContent.Create(new { Status = (int)status, Notes = notes, EndCall = endCall });

        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}

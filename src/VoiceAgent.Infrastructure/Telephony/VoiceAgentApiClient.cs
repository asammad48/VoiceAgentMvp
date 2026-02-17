using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Infrastructure.Telephony;

public sealed class VoiceAgentApiClient : IVoiceAgentApiClient
{
    private readonly ILogger<VoiceAgentApiClient> _log;
    private readonly HttpClient _http;
    private readonly Guid _tenantId;

    public VoiceAgentApiClient(ILogger<VoiceAgentApiClient> log, HttpClient http, string tenantId)
    {
        _log = log;
        _http = http;
        if (Guid.TryParse(tenantId, out var tid))
        {
            _tenantId = tid;
        }

        _http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
    }

    public async Task<Guid> InboundStartAsync(string campaign, string callerNumber, Guid? agentId, Guid? tenantId, CancellationToken ct)
    {
        var req = new
        {
            CampaignCode = campaign,
            CallerNumber = callerNumber,
            AgentId = agentId,
            TenantId = tenantId
        };

        var resp = await _http.PostAsJsonAsync("/v1/calls/inbound-start", req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<InboundStartResponse>(cancellationToken: ct);
        return body?.CallId ?? throw new Exception("Failed to get callId from inbound-start");
    }

    public async Task<AgentAction> GetIntroAsync(Guid callId, CancellationToken ct)
    {
        var resp = await _http.PostAsync($"/v1/calls/{callId}/intro", null, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AgentAction>(cancellationToken: ct) ?? AgentAction.SafeFallback();
    }

    public async Task<AgentAction> GetNextActionAsync(Guid callId, string transcript, Dictionary<string, string>? fields, CancellationToken ct)
    {
        var req = new { Transcript = transcript, Fields = fields };
        var resp = await _http.PostAsJsonAsync($"/v1/calls/{callId}/next", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AgentAction>(cancellationToken: ct) ?? AgentAction.SafeFallback();
    }

    public async Task UpdateStatusAsync(Guid callId, CallStatus status, string? notes, bool endCall, CancellationToken ct)
    {
        var req = new { Status = status, Notes = notes, EndCall = endCall };
        var resp = await _http.PostAsJsonAsync($"/v1/calls/{callId}/status", req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record InboundStartResponse(Guid CallId);
}

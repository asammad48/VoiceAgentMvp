using VoiceAgent.Domain.Models.Api;

namespace VoiceAgent.Domain.Ports;

public interface IVoiceAgentApiClient
{
    Task<CallDto?> ClaimNextCallAsync(Guid tenantId, CancellationToken ct);
    Task<AgentActionDto> GetNextActionAsync(Guid tenantId, Guid callId, string transcript, Dictionary<string, string>? fields, CancellationToken ct);
    Task UpdateStatusAsync(Guid tenantId, Guid callId, CallStatusDto status, string? notes = null, bool endCall = false, CancellationToken ct = default);
}

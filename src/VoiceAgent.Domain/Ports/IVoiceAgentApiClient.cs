using VoiceAgent.Domain.Models.Conversation;

namespace VoiceAgent.Domain.Ports;

public interface IVoiceAgentApiClient
{
    Task<Guid> InboundStartAsync(string campaign, string callerNumber, Guid? agentId, Guid? tenantId, CancellationToken ct);
    Task<AgentAction> GetIntroAsync(Guid callId, CancellationToken ct);
    Task<AgentAction> GetNextActionAsync(Guid callId, string transcript, Dictionary<string, string>? fields, CancellationToken ct);
    Task UpdateStatusAsync(Guid callId, CallStatus status, string? notes, bool endCall, CancellationToken ct);
}

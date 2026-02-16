using VoiceAgent.Domain.Models.Conversation;

namespace VoiceAgent.Domain.Ports;

public interface ILlmProvider
{
    Task<string> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct);
}

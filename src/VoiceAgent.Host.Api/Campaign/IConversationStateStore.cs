using VoiceAgent.Host.Api.Storage;

namespace VoiceAgent.Host.Api.Campaign;

public interface IConversationStateStore
{
    string GetCurrentStage(Call call);
    void SetCurrentStage(Call call, string stage);
}

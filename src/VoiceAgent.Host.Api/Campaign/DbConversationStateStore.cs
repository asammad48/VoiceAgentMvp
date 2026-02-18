using VoiceAgent.Host.Api.Storage;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class DbConversationStateStore : IConversationStateStore
{
    public string GetCurrentStage(Call call)
    {
        return call.CurrentStage ?? "Greeting";
    }

    public void SetCurrentStage(Call call, string stage)
    {
        call.CurrentStage = stage;
    }
}

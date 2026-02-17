namespace VoiceAgent.Domain.Models.Conversation;

public sealed class AgentAction
{
    public string? Say { get; set; }
    public string? Intent { get; set; }
    public Dictionary<string, string>? Fields { get; set; }
    public string? NextStep { get; set; }

    public static AgentAction SafeFallback() => new()
    {
        Say = "I can help with that by connecting you to a licensed agent. What’s a good callback time today or tomorrow?",
        Intent = "transfer",
        Fields = new Dictionary<string, string>(),
        NextStep = "request callback time"
    };
}

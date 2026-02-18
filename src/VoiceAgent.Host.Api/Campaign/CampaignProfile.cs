namespace VoiceAgent.Host.Api.Campaign;

public sealed class CampaignProfile
{
    public string Name { get; set; } = "";
    public List<string> AllowedIntents { get; set; } = new();
    public List<string> RequiredFields { get; set; } = new();
    public List<string> OptionalFields { get; set; } = new();
    public Dictionary<string, string> StageMap { get; set; } = new();
    public List<string> BannedPhrases { get; set; } = new();
    public string SystemAddon { get; set; } = "";
    public List<string> Script { get; set; } = new();
    public string? IntroPitch { get; set; }
}

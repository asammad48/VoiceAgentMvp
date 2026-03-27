using System.Text.Json;

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

    public List<EligibilityRule> EligibilityRules { get; set; } = new();
    public Dictionary<string, List<QuestionRule>> QuestionRules { get; set; } = new();
    public Dictionary<string, FieldPolicy> FieldPolicies { get; set; } = new();
}

public sealed class EligibilityRule
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = ""; // between, eq, neq, gt, lt
    public object? Min { get; set; }
    public object? Max { get; set; }
    public object? Value { get; set; }
    public string Reason { get; set; } = "Not eligible";
}

public sealed class QuestionRule
{
    public JsonElement? When { get; set; } // Can be a WhenCondition object or "default" string
    public string Ask { get; set; } = "";
    public List<string> Targets { get; set; } = new();
}

public sealed class WhenCondition
{
    public string? Field { get; set; }
    public string? Op { get; set; } // eq, neq
    public object? Value { get; set; }
}

public sealed class FieldPolicy
{
    public string ConfirmMode { get; set; } = "EndOnly"; // Never, EndOnly, ImmediateOnConflict, Always
}

using System.Text.Json;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class ResponseGuard
{
    public AgentAction Enforce(string raw, CampaignProfile profile)
    {
        var json = ExtractJsonObject(raw);
        if (json is null) return AgentAction.SafeFallback();

        try
        {
            var action = JsonSerializer.Deserialize<AgentAction>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? AgentAction.SafeFallback();

            action.Say ??= "";
            action.Intent ??= "transfer";
            action.NextStep ??= "request callback";
            action.Fields ??= new Dictionary<string, string>();

            if (!profile.AllowedIntents.Contains(action.Intent.Trim(), StringComparer.OrdinalIgnoreCase))
                return AgentAction.SafeFallback();

            var sayLower = action.Say.ToLowerInvariant();
            foreach (var banned in profile.BannedPhrases)
            {
                if (banned.Length > 0 && sayLower.Contains(banned.ToLowerInvariant()))
                    return AgentAction.SafeFallback();
            }

            if (action.Say.Length > 350) action.Say = action.Say.Substring(0, 350).Trim();

            if (action.Say.Contains("http", StringComparison.OrdinalIgnoreCase) || action.Say.Contains("www.", StringComparison.OrdinalIgnoreCase))
                return AgentAction.SafeFallback();

            return action;
        }
        catch { return AgentAction.SafeFallback(); }
    }

    private static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        if (start < 0) return null;
        int depth = 0;
        for (int i = start; i < raw.Length; i++)
        {
            if (raw[i] == '{') depth++;
            else if (raw[i] == '}')
            {
                depth--;
                if (depth == 0) return raw.Substring(start, i - start + 1);
            }
        }
        return null;
    }
}

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

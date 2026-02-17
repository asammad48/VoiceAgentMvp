using System.Text.Json;
using VoiceAgent.Domain.Models.Conversation;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class ResponseGuard
{
    public AgentAction Enforce(string raw, CampaignProfile profile)
    {
        // Must be JSON. If model returns extra text, try to extract first JSON object.
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
            action.NextStep ??= "request callback time";
            action.Fields ??= new Dictionary<string, string>();

            var intent = action.Intent.Trim();
            if (!profile.AllowedIntents.Contains(intent, StringComparer.OrdinalIgnoreCase))
                return AgentAction.SafeFallback();

            // Banned phrase scan (simple)
            var sayLower = action.Say.ToLowerInvariant();
            foreach (var banned in profile.BannedPhrases)
            {
                var b = banned.ToLowerInvariant();
                if (b.Length == 0) continue;
                if (sayLower.Contains(b))
                    return AgentAction.SafeFallback();
            }

            // Trim overly long speech (keep it phone-short)
            if (action.Say.Length > 350) action.Say = action.Say.Substring(0, 350).Trim();

            return action;
        }
        catch(Exception ex)
        {
            Console.WriteLine(ex);
            return AgentAction.SafeFallback();
        }
    }

    private static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();

        // If starts with { and ends with }, assume JSON
        if (raw.StartsWith("{") && raw.EndsWith("}")) return raw;

        // Try to find first {...}
        var start = raw.IndexOf('{');
        if (start < 0) return null;

        int depth = 0;
        for (int i = start; i < raw.Length; i++)
        {
            if (raw[i] == '{') depth++;
            else if (raw[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return raw.Substring(start, i - start + 1);
                }
            }
        }
        return null;
    }
}


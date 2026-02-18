using System.Text.Json;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class ResponseGuard
{
    public AgentAction Enforce(string raw, CampaignProfile profile, string currentStage, IReadOnlyDictionary<string, string> fields, out string? violation)
    {
        violation = null;
        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            violation = "Invalid JSON format";
            return AgentAction.SafeFallback(currentStage);
        }

        try
        {
            var action = JsonSerializer.Deserialize<AgentAction>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? AgentAction.SafeFallback(currentStage);

            action.Say ??= "";
            action.Intent ??= "transfer";
            action.NextStep ??= "request callback";
            action.Fields ??= new Dictionary<string, string>();

            // Rule: Allowed Intents
            if (!profile.AllowedIntents.Contains(action.Intent.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                violation = $"Intent '{action.Intent}' is not allowed for this campaign.";
                return AgentAction.SafeFallback(currentStage);
            }

            // Rule: if Stage != Greeting then intent=greet is invalid
            if (currentStage != "Greeting" && action.Intent.Equals("greet", StringComparison.OrdinalIgnoreCase))
            {
                violation = "Cannot use 'greet' intent after the Greeting stage.";
                return AgentAction.SafeFallback(currentStage);
            }

            // Rule: if ConsentConfirmed=true then agent must never ask “is now a bad time?” again
            var consentConfirmed = fields.TryGetValue("consent", out var c) && (c.Equals("true", StringComparison.OrdinalIgnoreCase) || c.Equals("yes", StringComparison.OrdinalIgnoreCase));
            if (consentConfirmed && action.Say.Contains("is now a bad time", StringComparison.OrdinalIgnoreCase))
            {
                violation = "Consent already confirmed. Do not ask if it is a bad time again.";
                return AgentAction.SafeFallback(currentStage);
            }

            // Rule: Field conflicts (age/state)
            if (action.Fields != null)
            {
                if (fields.TryGetValue("age_range", out var oldAge) && action.Fields.TryGetValue("age_range", out var newAge))
                {
                    if (IsAgeConflict(oldAge, newAge))
                    {
                        violation = $"Age conflict detected: was {oldAge}, now {newAge}. Please clarify.";
                        return AgentAction.SafeFallback(currentStage, violation);
                    }
                }
                if (fields.TryGetValue("state", out var oldState) && action.Fields.TryGetValue("state", out var newState))
                {
                    if (!string.Equals(oldState, newState, StringComparison.OrdinalIgnoreCase))
                    {
                        violation = $"State conflict detected: was {oldState}, now {newState}. Please clarify.";
                        return AgentAction.SafeFallback(currentStage, violation);
                    }
                }
            }

            var sayLower = action.Say.ToLowerInvariant();
            foreach (var banned in profile.BannedPhrases)
            {
                if (banned.Length > 0 && sayLower.Contains(banned.ToLowerInvariant()))
                {
                    violation = $"Banned phrase detected: {banned}";
                    return AgentAction.SafeFallback(currentStage);
                }
            }

            if (action.Say.Length > 350) action.Say = action.Say.Substring(0, 350).Trim();

            if (action.Say.Contains("http", StringComparison.OrdinalIgnoreCase) || action.Say.Contains("www.", StringComparison.OrdinalIgnoreCase))
            {
                violation = "Links are not allowed in agent speech.";
                return AgentAction.SafeFallback(currentStage);
            }

            return action;
        }
        catch (Exception ex)
        {
            violation = "Deserialization error: " + ex.Message;
            return AgentAction.SafeFallback(currentStage);
        }
    }

    private bool IsAgeConflict(string oldAge, string newAge)
    {
        // Simple heuristic: if they are significantly different numbers
        // Extract numbers from strings
        var oldNums = GetNumbers(oldAge);
        var newNums = GetNumbers(newAge);
        if (oldNums.Count == 0 || newNums.Count == 0) return false;

        // If any number in new set is very different from any in old set
        foreach (var n in newNums)
        {
            bool foundClose = false;
            foreach (var o in oldNums)
            {
                if (Math.Abs(n - o) <= 5) { foundClose = true; break; }
            }
            if (!foundClose) return true;
        }
        return false;
    }

    private List<int> GetNumbers(string input)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(input, @"\d+");
        var list = new List<int>();
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (int.TryParse(m.Value, out var val)) list.Add(val);
        }
        return list;
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

    public static AgentAction SafeFallback(string stage, string? clarification = null)
    {
        var say = clarification != null
            ? $"I'm sorry, I might have misunderstood. {clarification}"
            : GetFallbackForStage(stage);

        return new AgentAction
        {
            Say = say,
            Intent = "qualify",
            Fields = new Dictionary<string, string>(),
            NextStep = "clarify information"
        };
    }

    private static string GetFallbackForStage(string stage)
    {
        return stage switch
        {
            "Greeting" => "Hello? I'm calling about your request for information. Can you hear me okay?",
            "Consent" => "I just need to confirm, is now a good time to discuss your options?",
            "QualifyAge" => "To see which programs you might be eligible for, could you tell me your age?",
            "QualifyState" => "Which state are you currently living in?",
            "QualifyCoverage" => "Do you currently have any life insurance or final expense coverage?",
            "SetCallback" => "I'd like to have a licensed agent give you more details. What would be a good time for a brief call?",
            _ => "I can help with that by connecting you to a licensed agent. What’s a good callback time today or tomorrow?"
        };
    }
}

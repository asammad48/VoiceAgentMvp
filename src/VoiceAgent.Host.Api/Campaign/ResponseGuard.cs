using System.Text.Json;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Services;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class ResponseGuard
{
    private readonly IFieldPolicyEngine _fieldPolicy;

    public ResponseGuard(IFieldPolicyEngine fieldPolicy)
    {
        _fieldPolicy = fieldPolicy;
    }

    public AgentAction Enforce(string raw, CampaignProfile profile, string currentStage, IReadOnlyDictionary<string, Storage.CallFieldValue> fields, out string? violation)
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
            var consentConfirmed = fields.TryGetValue("consent", out var c) && (c.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true || c.Value?.ToString()?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true);
            if (consentConfirmed && action.Say.Contains("is now a bad time", StringComparison.OrdinalIgnoreCase))
            {
                violation = "Consent already confirmed. Do not ask if it is a bad time again.";
                return AgentAction.SafeFallback(currentStage);
            }

            // Rule: Generalized Field conflicts using FieldPolicyEngine
            if (action.Fields != null && action.Fields.Count > 0)
            {
                var domainFields = fields.ToDictionary(k => k.Key, v => new DomainFieldValue { Value = v.Value.Value, Confirmed = v.Value.Confirmed });
                var results = _fieldPolicy.ProcessUpdates(profile.Name, currentStage, domainFields, action.Fields);

                var conflict = results.FirstOrDefault(r => r.Conflict);
                if (conflict != null)
                {
                    violation = conflict.ClarificationQuestion ?? $"{conflict.FieldName} conflict detected. Please clarify.";
                    return AgentAction.SafeFallback(currentStage, violation);
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

using System.Text.Json;

namespace VoiceAgent.Host.Api.Campaign;

public sealed record NextStep(
    string NextStage,
    string? NextQuestionKey = null,
    string? RequiredFieldKey = null,
    string? SecondaryFieldKey = null,
    string? AskTemplate = null);

public interface INextStepPlanner
{
    NextStep PlanNext(string? campaignCode, string currentStage, IReadOnlyDictionary<string, Storage.CallFieldValue> fields);
}

public sealed class NextStepPlanner : INextStepPlanner
{
    private readonly CampaignRegistry _registry;

    public NextStepPlanner(CampaignRegistry registry)
    {
        _registry = registry;
    }

    public NextStep PlanNext(string? campaignCode, string currentStage, IReadOnlyDictionary<string, Storage.CallFieldValue> fields)
    {
        var code = campaignCode?.ToUpperInvariant() ?? "FE";
        var profile = _registry.Get(code);

        // 1. Check if we should stay in current stage due to missing fields in its question rule
        var currentRule = MatchQuestionRule(profile, currentStage, fields);
        if (currentRule != null)
        {
            var missingInRule = currentRule.Targets.Where(f => !IsFilled(f, fields)).ToList();
            if (missingInRule.Count > 0)
            {
                return new NextStep(currentStage, currentStage, missingInRule[0], missingInRule.Count > 1 ? missingInRule[1] : null, currentRule.Ask);
            }
        }

        // 2. Find the first missing required field
        string? firstMissingField = null;
        foreach (var field in profile.RequiredFields)
        {
            if (!IsFilled(field, fields))
            {
                firstMissingField = field;
                break;
            }
        }

        if (firstMissingField != null)
        {
            if (profile.StageMap.TryGetValue(firstMissingField, out var mappedStage))
            {
                var rule = MatchQuestionRule(profile, mappedStage, fields);
                if (rule != null)
                {
                    var missingInRule = rule.Targets.Where(f => !IsFilled(f, fields)).ToList();
                    if (missingInRule.Count > 0)
                    {
                        return new NextStep(mappedStage, mappedStage, missingInRule[0], missingInRule.Count > 1 ? missingInRule[1] : null, rule.Ask);
                    }
                }

                // Default multi-slot: try to find another missing field in same stage
                string? secondaryField = null;
                foreach (var field in profile.RequiredFields)
                {
                    if (field == firstMissingField) continue;
                    if (!IsFilled(field, fields))
                    {
                        if (profile.StageMap.TryGetValue(field, out var stage) && stage == mappedStage)
                        {
                            secondaryField = field;
                            break;
                        }
                    }
                }

                return new NextStep(mappedStage, mappedStage, firstMissingField, secondaryField);
            }

            return new NextStep(currentStage, currentStage, firstMissingField);
        }

        if (currentStage == "End") return new NextStep("End");
        return new NextStep(CampaignStages.FinalConfirm, CampaignStages.FinalConfirm);
    }

    private bool IsFilled(string field, IReadOnlyDictionary<string, Storage.CallFieldValue> fields)
    {
        return fields.TryGetValue(field, out var val) && val.Value != null && !string.IsNullOrWhiteSpace(val.Value.ToString());
    }

    private QuestionRule? MatchQuestionRule(CampaignProfile profile, string stage, IReadOnlyDictionary<string, Storage.CallFieldValue> fields)
    {
        if (!profile.QuestionRules.TryGetValue(stage, out var rules)) return null;

        foreach (var rule in rules)
        {
            if (rule.When == null) continue;

            if (rule.When.Value.ValueKind == JsonValueKind.String && rule.When.Value.GetString() == "default")
            {
                return rule;
            }

            var condition = JsonSerializer.Deserialize<WhenCondition>(rule.When.Value.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (condition != null && MatchCondition(condition, fields))
            {
                return rule;
            }
        }

        return null;
    }

    private bool MatchCondition(WhenCondition condition, IReadOnlyDictionary<string, Storage.CallFieldValue> fields)
    {
        if (string.IsNullOrEmpty(condition.Field)) return false;
        if (!fields.TryGetValue(condition.Field.ToLowerInvariant(), out var fieldVal) || fieldVal.Value == null) return false;

        var sVal = GetStringValue(fieldVal.Value);
        var sTarget = GetStringValue(condition.Value);

        return condition.Op?.ToLowerInvariant() switch
        {
            "eq" => string.Equals(sVal, sTarget, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(sVal, sTarget, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private string GetStringValue(object? val)
    {
        if (val == null) return "";
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String) return je.GetString() ?? "";
            return je.GetRawText().Trim('"');
        }
        return val.ToString() ?? "";
    }
}

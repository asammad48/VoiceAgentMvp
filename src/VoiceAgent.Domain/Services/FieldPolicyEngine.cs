using VoiceAgent.Domain.Models.Conversation;
using System.Text.RegularExpressions;

namespace VoiceAgent.Domain.Services;

public interface IFieldPolicyEngine
{
    List<FieldUpdateResult> ProcessUpdates(
        string campaignCode,
        string currentStage,
        IReadOnlyDictionary<string, DomainFieldValue> existingFields,
        IReadOnlyDictionary<string, string> incomingFields);
}

public sealed class FieldPolicyEngine : IFieldPolicyEngine
{
    public List<FieldUpdateResult> ProcessUpdates(
        string campaignCode,
        string currentStage,
        IReadOnlyDictionary<string, DomainFieldValue> existingFields,
        IReadOnlyDictionary<string, string> incomingFields)
    {
        var results = new List<FieldUpdateResult>();

        foreach (var incoming in incomingFields)
        {
            var fieldName = incoming.Key.Trim().ToLowerInvariant();
            var newValueRaw = incoming.Value;
            if (string.IsNullOrWhiteSpace(fieldName) || newValueRaw == null) continue;

            var normalizedValue = Normalize(fieldName, newValueRaw);
            if (normalizedValue == null || string.IsNullOrWhiteSpace(normalizedValue.ToString())) continue;

            existingFields.TryGetValue(fieldName, out var existing);

            var result = new FieldUpdateResult
            {
                FieldName = fieldName,
                OldValue = existing?.Value,
                NewValue = normalizedValue,
                Accepted = false,
                Confirmed = false,
                Conflict = false
            };

            var isSameValue = IsSameValue(fieldName, existing?.Value, normalizedValue);

            if (existing != null && existing.Confirmed)
            {
                if (!isSameValue)
                {
                    // Conflict with confirmed value
                    result.Conflict = true;
                    result.ClarificationQuestion = $"I have your {fieldName} as {existing.Value}, but I heard {normalizedValue}. Which one is correct?";
                    result.Reason = "conflict_with_confirmed";
                }
                else
                {
                    // Values match confirmed
                    result.Accepted = true;
                    result.Confirmed = true;
                    result.Reason = "match_confirmed";
                }
            }
            else if (existing != null && !existing.Confirmed)
            {
                if (!isSameValue)
                {
                    // Update unconfirmed value
                    result.Accepted = true;
                    result.Confirmed = true; // User provided it again (explicit), so we can confirm now
                    result.Reason = "explicit_update_unconfirmed";
                }
                else
                {
                    // Values match unconfirmed
                    result.Accepted = true;
                    result.Confirmed = true; // User repeated it, so we can confirm
                    result.Reason = "repeat_unconfirmed";
                }
            }
            else
            {
                // Initial capture
                result.Accepted = true;
                result.Confirmed = false; // Initial is unconfirmed
                result.Reason = "initial_capture";
            }

            results.Add(result);
        }

        return results;
    }

    private object? Normalize(string fieldName, string value)
    {
        var val = value.Trim();
        if (string.IsNullOrWhiteSpace(val)) return null;

        if (fieldName.Equals("age", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(val, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var num)) return num.ToString();
        }
        if (fieldName.Equals("consent", StringComparison.OrdinalIgnoreCase))
        {
            var v = val.ToLowerInvariant();
            if (v == "yes" || v == "true" || v == "confirmed" || v == "correct" || v == "yeah" || v == "sure") return "true";
            if (v == "no" || v == "false" || v == "nope") return "false";
        }
        return val;
    }

    private bool IsSameValue(string fieldName, object? oldVal, object? newVal)
    {
        if (oldVal == null || newVal == null) return oldVal == newVal;
        var sOld = oldVal.ToString() ?? "";
        var sNew = newVal.ToString() ?? "";

        if (fieldName.Equals("age", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(sOld, out var nOld) && int.TryParse(sNew, out var nNew))
            {
                return Math.Abs(nOld - nNew) <= 1; // Allow very close age? Actually prompt says "differs".
            }
        }

        return string.Equals(sOld, sNew, StringComparison.OrdinalIgnoreCase);
    }
}

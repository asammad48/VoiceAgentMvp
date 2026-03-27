using System.Text.Json;
using VoiceAgent.Host.Api.Storage;

namespace VoiceAgent.Host.Api.Campaign;

public sealed record EligibilityResult(bool Eligible, string? Reason = null);

public sealed class EligibilityEvaluator
{
    public EligibilityResult Evaluate(CampaignProfile profile, IReadOnlyDictionary<string, CallFieldValue> fields)
    {
        foreach (var rule in profile.EligibilityRules)
        {
            if (!fields.TryGetValue(rule.Field.ToLowerInvariant(), out var fieldVal) || fieldVal.Value == null)
            {
                continue; // Field not yet known, cannot disqualify yet
            }

            var val = fieldVal.Value;
            var isEligible = rule.Op.ToLowerInvariant() switch
            {
                "between" => IsBetween(val, rule.Min, rule.Max),
                "eq" => IsEqual(val, rule.Value),
                "neq" => !IsEqual(val, rule.Value),
                "gt" => Compare(val, rule.Value) > 0,
                "lt" => Compare(val, rule.Value) < 0,
                _ => true
            };

            if (!isEligible)
            {
                return new EligibilityResult(false, rule.Reason);
            }
        }

        return new EligibilityResult(true);
    }

    private bool IsBetween(object val, object? min, object? max)
    {
        var v = ToDouble(val);
        var mn = ToDouble(min);
        var mx = ToDouble(max);
        return v >= mn && v <= mx;
    }

    private bool IsEqual(object val, object? target)
    {
        if (target == null) return false;
        var sVal = GetStringValue(val);
        var sTarget = GetStringValue(target);
        return string.Equals(sVal, sTarget, StringComparison.OrdinalIgnoreCase);
    }

    private int Compare(object val, object? target)
    {
        var v = ToDouble(val);
        var t = ToDouble(target);
        return v.CompareTo(t);
    }

    private double ToDouble(object? val)
    {
        if (val == null) return 0;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number) return je.GetDouble();
            if (je.ValueKind == JsonValueKind.String && double.TryParse(je.GetString(), out var d)) return d;
            return 0;
        }
        if (double.TryParse(val.ToString(), out var d2)) return d2;
        return 0;
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

namespace VoiceAgent.Host.Api.Campaign;

public sealed record NextStep(string NextStage, string? NextQuestionKey = null, string? RequiredFieldKey = null);

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
        var order = CampaignStages.GetOrderForCampaign(code);

        // 1. Find the first missing required field
        string? firstMissingField = null;
        foreach (var field in profile.RequiredFields)
        {
            if (!fields.ContainsKey(field) || fields[field].Value == null || string.IsNullOrWhiteSpace(fields[field].Value?.ToString()))
            {
                firstMissingField = field;
                break;
            }
        }

        if (firstMissingField != null)
        {
            // Go to the stage mapped for this field
            if (profile.StageMap.TryGetValue(firstMissingField, out var mappedStage))
            {
                return new NextStep(mappedStage, mappedStage, firstMissingField);
            }

            // If not in stage map, just use a default stage name derived from field or current
            return new NextStep(currentStage, currentStage, firstMissingField);
        }

        // All required fields present -> FinalConfirm (unless already there or at End)
        if (currentStage == "End") return new NextStep("End");
        return new NextStep(CampaignStages.FinalConfirm, CampaignStages.FinalConfirm);
    }
}

using VoiceAgent.Domain.Models.Conversation;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class PromptBuilder
{
    public List<ChatTurn> BuildTurns(
        CampaignProfile profile,
        string direction,
        string agentName,
        string leadName,
        IReadOnlyDictionary<string, string> fields,
        string lastUserUtterance,
        bool isFirstTurn,
        NextStep nextStep)
    {
        var isGreetingStage = nextStep.NextStage == "Greeting";
        var consentConfirmed = fields.TryGetValue("consent", out var c) && (c.Equals("true", StringComparison.OrdinalIgnoreCase) || c.Equals("yes", StringComparison.OrdinalIgnoreCase));
        var intro = (profile.IntroPitch ?? "").Replace("{lead_name}", leadName).Replace("{agent_name}", agentName);

        var fieldsToExtract = new HashSet<string>(profile.RequiredFields);
        if (nextStep.RequiredFieldKey != null) fieldsToExtract.Add(nextStep.RequiredFieldKey);

        var scriptLines = profile.Script;
        if (!isGreetingStage || consentConfirmed)
        {
            // Remove lines that look like greetings or consent requests
            scriptLines = scriptLines.Where(line =>
                !line.Contains(agentName, StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Hi ", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Hi,", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Hello", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Is now a bad time", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        var sys = $@"
You are a phone agent for an {direction} call. Be brief, calm, human.
You MUST follow the campaign rules and script. If asked anything outside scope, offer a licensed agent callback/transfer.
Never invent prices, benefits, eligibility, plan availability, or legal/medical advice.
Never claim government affiliation.

Return JSON ONLY with this schema:
{{
  ""say"": ""what to speak to the lead"",
  ""intent"": ""one_of: {string.Join("|", profile.AllowedIntents)}"",
  ""fields"": {{ {string.Join(", ", fieldsToExtract.Select(f => $"\"{f}\": \"\""))} }},
  ""next_step"": ""what you will do next""
}}

{profile.SystemAddon}

CURRENT STAGE: {nextStep.NextStage}
{(nextStep.RequiredFieldKey != null ? $"GOAL: Collect the field '{nextStep.RequiredFieldKey}'." : "")}
NEXT_QUESTION_KEY: {nextStep.NextQuestionKey}

{(scriptLines.Length > 0 ? "SCRIPT (follow this structure):\n- " + string.Join("\n- ", scriptLines) : "")}

AGENT NAME: {agentName}
LEAD NAME: {leadName}

CURRENT KNOWN FIELDS:
{string.Join("\n", fields.Select(kv => $"{kv.Key}={kv.Value}"))}

{(isFirstTurn && isGreetingStage && !string.IsNullOrWhiteSpace(intro) ? $"You just started the call by saying: \"{intro}\". The user responded with what follows." : "")}
".Trim();

        return new List<ChatTurn>
        {
            new(ChatRole.System, sys),
            new(ChatRole.User, lastUserUtterance)
        };
    }
}

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
        var intro = (profile.IntroPitch ?? "").Replace("{lead_name}", leadName).Replace("{agent_name}", agentName);

        var fieldsToExtract = new HashSet<string>(profile.RequiredFields);
        if (nextStep.RequiredFieldKey != null) fieldsToExtract.Add(nextStep.RequiredFieldKey);

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

SCRIPT (follow this structure):
- {string.Join("\n- ", profile.Script)}

AGENT NAME: {agentName}
LEAD NAME: {leadName}

CURRENT KNOWN FIELDS (do not assume missing values):
{string.Join("\n", fields.Select(kv => $"{kv.Key}={kv.Value}"))}

{(isFirstTurn && !string.IsNullOrWhiteSpace(intro) ? $"You just started the call by saying: \"{intro}\". The user responded with what follows." : "")}
".Trim();

        return new List<ChatTurn>
        {
            new(ChatRole.System, sys),
            new(ChatRole.User, lastUserUtterance)
        };
    }
}

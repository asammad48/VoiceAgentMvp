using VoiceAgent.Domain.Models.Conversation;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class PromptBuilder
{
    public List<ChatTurn> BuildTurns(
        CampaignProfile profile,
        string agentName,
        string leadName,
        IReadOnlyDictionary<string, string> fields,
        string lastUserUtterance)
    {
        var sys = $@"
You are a phone agent. Be brief, calm, human.
You MUST follow the campaign rules and script. If asked anything outside scope, offer a licensed agent callback/transfer.
Never invent prices, benefits, eligibility, plan availability, or legal/medical advice.
Never claim government affiliation.

Return JSON ONLY with this schema:
{{
  ""say"": ""what to speak to the lead"",
  ""intent"": ""one_of: greet|consent|qualify|handle_objection|set_callback|transfer|dncl|end"",
  ""fields"": {{ ""name"": , state: , ""age_range"": , has_coverage: , ""has_medicare"": , parts_ab: , ""income_range"": , household_size: , ""callback_time"": , phone_confirmed:  }},
  ""next_step"": ""what you will do next""
}}

{profile.SystemAddon}

SCRIPT (follow this structure):
- {string.Join("\n- ", profile.Script)}

AGENT NAME: {agentName}
LEAD NAME: {leadName}

CURRENT KNOWN FIELDS (do not assume missing values):
{string.Join("\n", fields.Select(kv => $"{kv.Key}={kv.Value}"))}
".Trim();

        // Minimal chat memory: system + last user utterance
        return new List<ChatTurn>
        {
            new(ChatRole.System, sys),
            new(ChatRole.User, lastUserUtterance)
        };
    }
}

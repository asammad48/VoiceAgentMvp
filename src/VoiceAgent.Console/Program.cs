using System.Net.Http.Json;
using System.Text.Json;

Console.WriteLine("=== VoiceAgent Outbound Console ===");

var apiBaseUrl = Environment.GetEnvironmentVariable("VOICEAGENT_API_URL") ?? "http://localhost:5000";

Console.Write($"API Base URL [{apiBaseUrl}]: ");
var inputUrl = Console.ReadLine();
if (!string.IsNullOrWhiteSpace(inputUrl)) apiBaseUrl = inputUrl;

Console.Write("Tenant ID (GUID): ");
var tenantIdStr = Console.ReadLine();
if (!Guid.TryParse(tenantIdStr, out var tenantId)) { Console.WriteLine("Invalid GUID"); return; }

Console.Write("Agent ID (GUID): ");
var agentIdStr = Console.ReadLine();
if (!Guid.TryParse(agentIdStr, out var agentId)) { Console.WriteLine("Invalid GUID"); return; }

Console.Write("Phone Number: ");
var phone = Console.ReadLine();
if (string.IsNullOrWhiteSpace(phone)) { Console.WriteLine("Phone is required"); return; }

Console.Write("Campaign (FE/ACA/MEDICARE/SOLAR/AUTOCARE): ");
var campaign = Console.ReadLine()?.ToUpperInvariant() ?? "FE";

using var http = new HttpClient();
http.BaseAddress = new Uri(apiBaseUrl);
http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

Console.WriteLine("Creating/Finding Lead...");
var leadResp = await http.PostAsJsonAsync("/v1/leads", new { Phone = phone, CampaignCode = campaign, Name = "Console Lead" });
if (!leadResp.IsSuccessStatusCode)
{
    Console.WriteLine("Failed to create/get lead: " + await leadResp.Content.ReadAsStringAsync());
    return;
}

var leadJson = await leadResp.Content.ReadFromJsonAsync<JsonElement>();
var leadId = leadJson.GetProperty("id").GetGuid();

Console.WriteLine("Queuing Outbound Call for Lead " + leadId + "...");
var callResp = await http.PostAsJsonAsync("/v1/calls/start", new
{
    LeadId = leadId,
    AgentId = agentId,
    Direction = 1, // Outbound
    CampaignCode = campaign,
    PhoneTo = phone,
    StartReason = "console"
});

if (callResp.IsSuccessStatusCode)
{
    var callJson = await callResp.Content.ReadFromJsonAsync<JsonElement>();
    Console.WriteLine("Outbound call queued successfully. Call ID: " + callJson.GetProperty("id").GetGuid());
}
else
{
    var err = await callResp.Content.ReadAsStringAsync();
    Console.WriteLine("Failed to queue call: " + err);
}

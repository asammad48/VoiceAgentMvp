using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoiceAgent.Infrastructure.Asterisk;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddHttpClient();
var sp = services.BuildServiceProvider();

var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("ConsoleDialer");
var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();

// Arguments
var tenantId = config["tenant"] ?? throw new Exception("--tenant <guid> is required");
var agentId = config["agentId"] ?? throw new Exception("--agentId <guid> is required");
var campaign = (config["campaign"] ?? "FE").ToUpperInvariant();
var endpoint = config["endpoint"] ?? "PJSIP/6001";
var callerId = config["callerId"] ?? "AI Agent <6000>";
var leadIdStr = config["leadId"];
var apiBaseUrl = config["Api:BaseUrl"] ?? "http://localhost:5000";
var ariBaseUrl = config["Asterisk:BaseUrl"] ?? "http://localhost:8088";
var ariUser = config["Asterisk:AriUser"] ?? "asterisk";
var ariPass = config["Asterisk:AriPassword"] ?? "asterisk";
var stasisApp = config["Asterisk:StasisApp"] ?? "aiapp";

Guid leadId;
if (string.IsNullOrEmpty(leadIdStr))
{
    log.LogInformation("LeadId missing, creating a new lead...");
    http.DefaultRequestHeaders.Clear();
    http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
    var leadResp = await http.PostAsJsonAsync($"{apiBaseUrl}/v1/leads", new
    {
        CampaignCode = campaign,
        Name = "John Doe",
        Phone = "1234567890"
    });
    leadResp.EnsureSuccessStatusCode();
    var lead = await leadResp.Content.ReadFromJsonAsync<JsonElement>();
    leadId = lead.GetProperty("id").GetGuid();
    log.LogInformation("Created lead: {LeadId}", leadId);
}
else
{
    leadId = Guid.Parse(leadIdStr);
}

// 1. Start call record in API
log.LogInformation("Starting call record in API...");
http.DefaultRequestHeaders.Clear();
http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
var callStartResp = await http.PostAsJsonAsync($"{apiBaseUrl}/v1/calls/start", new
{
    LeadId = leadId,
    AgentId = Guid.Parse(agentId),
    CampaignCode = campaign
});
callStartResp.EnsureSuccessStatusCode();
var call = await callStartResp.Content.ReadFromJsonAsync<JsonElement>();
var callId = call.GetProperty("id").GetGuid();
log.LogInformation("Call record created: {CallId}", callId);

// 2. Originate via ARI
log.LogInformation("Originating call via ARI to {Endpoint}...", endpoint);
var ariClient = new AriClient(
    sp.GetRequiredService<ILogger<AriClient>>(),
    http,
    new Uri(ariBaseUrl),
    ariUser,
    ariPass);

var variables = new Dictionary<string, string>
{
    ["CALL_ID"] = callId.ToString(),
    ["CAMPAIGN"] = campaign,
    ["TENANT_ID"] = tenantId
};

await ariClient.OriginateAsync(endpoint, stasisApp, callerId, variables, CancellationToken.None);
log.LogInformation("Call originated successfully.");

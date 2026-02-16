using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Ports;
using VoiceAgent.Host.Api.Campaign;
using VoiceAgent.Host.Api.Storage;
using VoiceAgent.Host.Api.Tenancy;
using VoiceAgent.Infrastructure.Providers.Deepgram;
using VoiceAgent.Infrastructure.Providers.ElevenLabs;
using VoiceAgent.Infrastructure.Providers.Llm;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    // Minimal APIs can sometimes generate duplicate ApiDescriptions.
    // This prevents "Sequence contains more than one matching element".
    o.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    // Make operationIds unique/stable (helps swagger + clients)
    o.CustomOperationIds(apiDesc =>
    {
        var method = apiDesc.HttpMethod ?? "GET";
        var path = apiDesc.RelativePath ?? "root";
        path = path.Replace("/", "_").Replace("{", "").Replace("}", "").Replace(":", "_");
        return $"{method}_{path}";
    });
    o.AddSecurityDefinition("TenantId", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "X-Tenant-Id",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Tenant Id header"
    });

    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "TenantId"
                }
            },
            Array.Empty<string>()
        }
    });
});


builder.Services.AddSingleton<TenantContext>();
builder.Services.AddSingleton<CampaignRegistry>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<ResponseGuard>();

// DB
builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
});

// Providers (API tests only)
var cfg = builder.Configuration;

builder.Services.AddHttpClient("hf");
builder.Services.AddSingleton<ILlmProvider>(sp =>
{
    var log = sp.GetRequiredService<ILogger<HfRouterLlmProvider>>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("hf");
    return new HfRouterLlmProvider(log, http, new Uri(cfg["HfRouter:Endpoint"]!), cfg["HfRouter:Token"]!, cfg["HfRouter:Model"]!);
});

builder.Services.AddHttpClient("xi");
builder.Services.AddSingleton<ITtsProvider>(sp =>
{
    var log = sp.GetRequiredService<ILogger<ElevenLabsTtsProvider>>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("xi");
    return new ElevenLabsTtsProvider(log, http, cfg["ElevenLabs:ApiKey"]!, cfg["ElevenLabs:VoiceId"]!, cfg["ElevenLabs:ModelId"]!);
});

builder.Services.AddHttpClient("dg");
builder.Services.AddSingleton<DeepgramPrerecordedStt>(sp =>
{
    var log = sp.GetRequiredService<ILogger<DeepgramPrerecordedStt>>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("dg");
    return new DeepgramPrerecordedStt(log, http, cfg["Deepgram:ApiKey"]!);
});

var app = builder.Build();

// Auto-create DB schema for MVP (no migrations). For production, use migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));

// Tenancy
app.UseMiddleware<TenantMiddleware>();

// ========== TENANTS (no header required) ==========
app.MapPost("/v1/tenants", async ([FromBody] CreateTenantRequest req, AppDbContext db) =>
{
    var t = new Tenant { Id = Guid.NewGuid(), Name = req.Name?.Trim() ?? "Tenant" };
    db.Tenants.Add(t);
    await db.SaveChangesAsync();
    return Results.Ok(new { tenantId = t.Id, name = t.Name });
}).WithOpenApi();

// ========== AGENTS ==========
app.MapPost("/v1/agents", async ([FromBody] CreateAgentRequest req, TenantContext tenant, AppDbContext db) =>
{
    var a = new Agent
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.TenantId,
        DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? "Agent" : req.DisplayName.Trim(),
        DefaultCampaignCode = req.DefaultCampaignCode?.ToUpperInvariant()
    };
    db.Agents.Add(a);
    await db.SaveChangesAsync();
    return Results.Ok(a);
}).WithOpenApi();

app.MapGet("/v1/agents", async (TenantContext tenant, AppDbContext db) =>
{
    var list = await db.Agents.Where(x => x.TenantId == tenant.TenantId).OrderByDescending(x => x.CreatedAt).ToListAsync();
    return Results.Ok(list);
}).WithOpenApi();

// ========== LEADS ==========
app.MapPost("/v1/leads", async ([FromBody] CreateLeadRequest req, TenantContext tenant, AppDbContext db) =>
{
    var lead = new Lead
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.TenantId,
        CampaignCode = (req.CampaignCode ?? "FE").ToUpperInvariant(),
        Name = req.Name?.Trim() ?? "",
        Phone = req.Phone?.Trim() ?? "",
        State = req.State?.Trim(),
        Status = CallStatus.New
    };
    db.Leads.Add(lead);
    await db.SaveChangesAsync();
    return Results.Ok(lead);
}).WithOpenApi();

app.MapGet("/v1/leads", async (
    [FromQuery] string? campaign,
    [FromQuery] CallStatus? status,
    TenantContext tenant,
    AppDbContext db) =>
{
    var q = db.Leads.AsQueryable().Where(x => x.TenantId == tenant.TenantId);
    if (!string.IsNullOrWhiteSpace(campaign)) q = q.Where(x => x.CampaignCode == campaign.ToUpper());
    if (status is not null) q = q.Where(x => x.Status == status);
    var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
    return Results.Ok(list);
}).WithOpenApi();

// ========== CALLS ==========
// Start a call record (for Swagger testing / simulation).
app.MapPost("/v1/calls/start", async ([FromBody] StartCallRequest req, TenantContext tenant, AppDbContext db) =>
{
    var lead = await db.Leads.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == req.LeadId);
    if (lead is null) return Results.NotFound(new { error = "lead not found" });

    var agent = await db.Agents.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == req.AgentId);
    if (agent is null) return Results.NotFound(new { error = "agent not found" });

    var call = new Call
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.TenantId,
        LeadId = lead.Id,
        AgentId = agent.Id,
        CampaignCode = (req.CampaignCode ?? lead.CampaignCode).ToUpperInvariant(),
        Status = CallStatus.Started
    };

    lead.Status = CallStatus.Started;

    db.Calls.Add(call);
    await db.SaveChangesAsync();
    return Results.Ok(call);
}).WithOpenApi();

// Main "next step" endpoint: send last user transcript -> get agent JSON action (bounded).
app.MapPost("/v1/calls/{callId:guid}/next", async (
    Guid callId,
    [FromBody] NextRequest req,
    TenantContext tenant,
    AppDbContext db,
    CampaignRegistry registry,
    PromptBuilder pb,
    ResponseGuard guard,
    ILlmProvider llm) =>
{
    var call = await db.Calls.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == callId);
    if (call is null) return Results.NotFound(new { error = "call not found" });

    var lead = await db.Leads.FirstAsync(x => x.TenantId == tenant.TenantId && x.Id == call.LeadId);
    var agent = await db.Agents.FirstAsync(x => x.TenantId == tenant.TenantId && x.Id == call.AgentId);

    var profile = registry.Get(call.CampaignCode);

    // Load existing fields
    var fields = await db.CallFields.Where(x => x.TenantId == tenant.TenantId && x.CallId == call.Id)
        .ToDictionaryAsync(x => x.Key, x => x.Value);

    // Merge incoming fields (client can pass current known fields)
    if (req.Fields is not null)
        foreach (var kv in req.Fields)
            fields[kv.Key] = kv.Value;

    var turns = pb.BuildTurns(profile, agent.DisplayName, lead.Name, fields, req.Transcript ?? "");

    // store user turn
    db.CallTurns.Add(new CallTurn
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.TenantId,
        CallId = call.Id,
        Role = "user",
        Text = req.Transcript ?? ""
    });

    var raw = await llm.CompleteAsync(turns, CancellationToken.None);
    var action = guard.Enforce(raw, profile);

    // store assistant turn
    db.CallTurns.Add(new CallTurn
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.TenantId,
        CallId = call.Id,
        Role = "assistant",
        Text = action.Say ?? ""
    });

    // upsert fields returned by model
    if (action.Fields is not null)
    {
        foreach (var kv in action.Fields)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null) continue;
            var key = kv.Key.Trim();
            var val = kv.Value.Trim();

            var existing = await db.CallFields.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.CallId == call.Id && x.Key == key);
            if (existing is null)
            {
                db.CallFields.Add(new CallField { Id = Guid.NewGuid(), TenantId = tenant.TenantId, CallId = call.Id, Key = key, Value = val });
            }
            else
            {
                existing.Value = val;
            }
        }
    }

    // Auto-status updates based on intent
    var intent = (action.Intent ?? "").ToLowerInvariant();
    if (intent == "dncl")
    {
        call.Status = CallStatus.Dnc;
        lead.Status = CallStatus.Dnc;
        call.EndedAt = DateTimeOffset.UtcNow;
    }
    else if (intent == "set_callback")
    {
        call.Status = CallStatus.CallbackScheduled;
        lead.Status = CallStatus.CallbackScheduled;
    }
    else if (intent == "transfer")
    {
        call.Status = CallStatus.Transferred;
        lead.Status = CallStatus.Transferred;
    }

    await db.SaveChangesAsync();
    return Results.Ok(action);
}).WithOpenApi();

// Manual status update (Not Interested etc.)
app.MapPost("/v1/calls/{callId:guid}/status", async (
    Guid callId,
    [FromBody] UpdateStatusRequest req,
    TenantContext tenant,
    AppDbContext db) =>
{
    var call = await db.Calls.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == callId);
    if (call is null) return Results.NotFound(new { error = "call not found" });

    call.Status = req.Status;
    call.Notes = req.Notes;
    if (req.EndCall) call.EndedAt = DateTimeOffset.UtcNow;

    var lead = await db.Leads.FirstAsync(x => x.TenantId == tenant.TenantId && x.Id == call.LeadId);
    lead.Status = req.Status;

    await db.SaveChangesAsync();
    return Results.Ok(call);
}).WithOpenApi();

app.MapGet("/v1/calls", async (TenantContext tenant, AppDbContext db) =>
{
    var list = await db.Calls.Where(x => x.TenantId == tenant.TenantId)
        .OrderByDescending(x => x.StartedAt).ToListAsync();
    return Results.Ok(list);
}).WithOpenApi();

// Existing utility endpoints from previous Swagger host
app.MapPost("/v1/chat", async ([FromBody] ChatRequest req, ILlmProvider llm) =>
{
    var turns = new List<ChatTurn>();
    if (!string.IsNullOrWhiteSpace(req.System))
        turns.Add(new ChatTurn(ChatRole.System, req.System!));

    foreach (var m in req.Messages ?? Array.Empty<ChatMessage>())
    {
        var role = (m.Role ?? "user").ToLowerInvariant() switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            _ => ChatRole.User
        };
        turns.Add(new ChatTurn(role, m.Content ?? ""));
    }

    var reply = await llm.CompleteAsync(turns, CancellationToken.None);
    return Results.Ok(new { reply });
}).WithOpenApi();

app.MapPost("/v1/tts", async ([FromBody] TtsRequest req, ITtsProvider tts, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Text))
        return Results.BadRequest(new { error = "text is required" });

    using var ms = new MemoryStream();
    var cap = req.MaxBytes ?? 120_000;
    await foreach (var frame in tts.SynthesizeMuLawAsync(req.Text!, ct))
    {
        await ms.WriteAsync(frame.Data, ct);
        if (ms.Length >= cap) break;
    }

    return Results.File(ms.ToArray(), "application/octet-stream", "tts.ulaw");
}).WithOpenApi();

app.MapPost("/v1/stt", async ([FromForm] SttFormRequest req, DeepgramPrerecordedStt stt, CancellationToken ct) =>
{
    if (req.File is null || req.File.Length == 0)
        return Results.BadRequest(new { error = "file is required" });

    var q = string.IsNullOrWhiteSpace(req.Query)
        ? "model=nova-2&smart_format=true&punctuate=true"
        : req.Query!;

    await using var stream = req.File.OpenReadStream();
    var json = await stt.TranscribeAsync(stream, req.File.ContentType ?? "application/octet-stream", q, ct);

    if (string.IsNullOrWhiteSpace(json))
        return Results.Problem("Deepgram STT failed. Check API key and logs.");

    return Results.Text(json, "application/json");
}).DisableAntiforgery().WithOpenApi();

app.Run();

// DTOs
public sealed record CreateTenantRequest(string? Name);
public sealed record CreateAgentRequest(string? DisplayName, string? DefaultCampaignCode);
public sealed record CreateLeadRequest(string? CampaignCode, string? Name, string? Phone, string? State);
public sealed record StartCallRequest(Guid LeadId, Guid AgentId, string? CampaignCode);
public sealed record NextRequest(string? Transcript, Dictionary<string, string>? Fields);
public sealed record UpdateStatusRequest(CallStatus Status, string? Notes, bool EndCall = true);

public sealed record ChatRequest(string? System, ChatMessage[]? Messages);
public sealed record ChatMessage(string? Role, string? Content);
public sealed record TtsRequest(string? Text, int? MaxBytes);

public sealed class SttFormRequest
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    [FromForm(Name = "query")]
    public string? Query { get; set; }
}

public sealed record TtsMp3Request(string Text, string? Voice);

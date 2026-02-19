using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoiceAgent.Domain.Models.Conversation;
using VoiceAgent.Domain.Ports;
using VoiceAgent.Domain.Services;
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
    o.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
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
builder.Services.AddSingleton<IFieldPolicyEngine, FieldPolicyEngine>();
builder.Services.AddSingleton<ResponseGuard>();
builder.Services.AddSingleton<IConversationStateStore, DbConversationStateStore>();
builder.Services.AddSingleton<INextStepPlanner, NextStepPlanner>();

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
});

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.UseMiddleware<TenantMiddleware>();

// ========== TENANTS ==========
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
    var phone = req.Phone?.Trim() ?? "";
    var isDnc = await db.DoNotCalls.AnyAsync(x => x.TenantId == tenant.TenantId && x.Phone == phone);

    var lead = new Lead
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.TenantId,
        CampaignCode = (req.CampaignCode ?? "FE").ToUpperInvariant(),
        Name = req.Name?.Trim() ?? "",
        Phone = phone,
        State = req.State?.Trim(),
        Status = isDnc ? CallStatus.Dnc : CallStatus.New
    };
    db.Leads.Add(lead);
    await db.SaveChangesAsync();
    return Results.Ok(lead);
}).WithOpenApi();

app.MapGet("/v1/leads", async ([FromQuery] string? campaign, [FromQuery] CallStatus? status, TenantContext tenant, AppDbContext db) =>
{
    var q = db.Leads.AsQueryable().Where(x => x.TenantId == tenant.TenantId);
    if (!string.IsNullOrWhiteSpace(campaign)) q = q.Where(x => x.CampaignCode == campaign.ToUpper());
    if (status is not null) q = q.Where(x => x.Status == status);
    var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
    return Results.Ok(list);
}).WithOpenApi();

// ========== CALLS ==========
app.MapPost("/v1/calls/start", async ([FromBody] StartCallRequest req, TenantContext tenant, AppDbContext db) =>
{
    var lead = await db.Leads.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == req.LeadId);
    if (lead is null) return Results.NotFound(new { error = "lead not found" });
    var agent = await db.Agents.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == req.AgentId);
    if (agent is null) return Results.NotFound(new { error = "agent not found" });

    var isDnc = await db.DoNotCalls.AnyAsync(x => x.TenantId == tenant.TenantId && x.Phone == lead.Phone);
    if (isDnc) return Results.BadRequest(new { error = "Phone is on DNC list" });

    var call = new Call
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.TenantId,
        LeadId = lead.Id,
        AgentId = agent.Id,
        Direction = req.Direction ?? CallDirection.Outbound,
        CampaignCode = (req.CampaignCode ?? lead.CampaignCode).ToUpperInvariant(),
        InboundUseCaseCode = req.InboundUseCaseCode,
        PhoneFrom = req.PhoneFrom,
        PhoneTo = req.PhoneTo ?? lead.Phone,
        StartReason = req.StartReason ?? "manual",
        Status = req.Direction == CallDirection.Inbound ? CallStatus.Started : CallStatus.New
    };
    lead.Status = call.Status;
    db.Calls.Add(call);
    await db.SaveChangesAsync();
    return Results.Ok(call);
}).WithOpenApi();

app.MapPost("/v1/calls/claim", async (TenantContext tenant, AppDbContext db, CampaignRegistry registry, IConfiguration config) =>
{
    // Max concurrent calls check
    var maxConcurrent = config.GetValue<int>("MaxConcurrentCalls", 10);
    var currentActive = await db.Calls.CountAsync(x => x.TenantId == tenant.TenantId && (x.Status == CallStatus.Started || x.Status == CallStatus.Connected));
    if (currentActive >= maxConcurrent) return Results.Problem("Max concurrent calls reached", statusCode: 429);

    var call = await db.Calls
        .Where(x => x.TenantId == tenant.TenantId && x.Status == CallStatus.New && x.Direction == CallDirection.Outbound)
        .OrderBy(x => x.StartedAt)
        .FirstOrDefaultAsync();

    if (call == null) return Results.NotFound();

    // Check DNC again just before originating
    var lead = await db.Leads.FirstAsync(l => l.Id == call.LeadId);
    var isDnc = await db.DoNotCalls.AnyAsync(d => d.TenantId == tenant.TenantId && d.Phone == lead.Phone);
    if (isDnc)
    {
        call.Status = CallStatus.DncBlocked;
        call.EndedAt = DateTimeOffset.UtcNow;
        lead.Status = CallStatus.Dnc;
        await db.SaveChangesAsync();
        return Results.Conflict(new { error = "DNC Blocked" });
    }

    call.Status = CallStatus.Started;
    await db.SaveChangesAsync();

    var profile = registry.Get(call.InboundUseCaseCode ?? call.CampaignCode);
    var agent = await db.Agents.FirstAsync(a => a.Id == call.AgentId);

    return Results.Ok(new
    {
        call.Id,
        call.TenantId,
        call.LeadId,
        call.AgentId,
        call.CampaignCode,
        call.InboundUseCaseCode,
        call.PhoneTo,
        call.PhoneFrom,
        LeadName = lead.Name,
        AgentName = agent.DisplayName,
        profile.IntroPitch
    });
}).WithOpenApi();

app.MapPost("/v1/calls/{callId:guid}/next", async (Guid callId, [FromBody] NextRequest req, TenantContext tenant, AppDbContext db, CampaignRegistry registry, PromptBuilder pb, ResponseGuard guard, ILlmProvider llm, IConversationStateStore stateStore, INextStepPlanner planner, IFieldPolicyEngine fieldPolicy, ILogger<Program> logger) =>
{
    var call = await db.Calls.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == callId);
    if (call is null) return Results.NotFound(new { error = "call not found" });
    var lead = await db.Leads.FirstAsync(x => x.TenantId == tenant.TenantId && x.Id == call.LeadId);
    var agent = await db.Agents.FirstAsync(x => x.TenantId == tenant.TenantId && x.Id == call.AgentId);
    var profile = registry.Get(call.InboundUseCaseCode ?? call.CampaignCode);

    // Load structured fields
    var fields = new Dictionary<string, CallFieldValue>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(call.FieldsJson))
    {
        fields = JsonSerializer.Deserialize<Dictionary<string, CallFieldValue>>(call.FieldsJson) ?? fields;
    }

    // Handle legacy/external fields if any
    if (req.Fields is not null)
    {
        foreach (var kv in req.Fields)
        {
            if (!fields.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            {
                fields[kv.Key] = new CallFieldValue { Value = kv.Value, Confirmed = true };
            }
        }
    }

    var currentStage = stateStore.GetCurrentStage(call);
    var nextStep = planner.PlanNext(call.InboundUseCaseCode ?? call.CampaignCode, currentStage, fields);

    var missingFields = profile.RequiredFields.Where(f => !fields.ContainsKey(f) || fields[f].Value == null).ToList();
    logger.LogInformation("[TURN DEBUG] Call: {CallId}, CurrentStage: {CurrentStage}, MissingFields: {MissingFields}, NextStageGoal: {NextStageGoal}",
        call.Id, currentStage, string.Join(",", missingFields), nextStep.NextStage);

    var turnCount = await db.CallTurns.CountAsync(x => x.CallId == call.Id && x.Role == "user");
    var turns = pb.BuildTurns(profile, call.Direction.ToString(), agent.DisplayName, lead.Name, fields, req.Transcript ?? "", turnCount == 0, nextStep);

    var userTurn = new CallTurn { Id = Guid.NewGuid(), TenantId = tenant.TenantId, CallId = call.Id, Role = "user", Text = req.Transcript ?? "" };
    db.CallTurns.Add(userTurn);
    var raw = await llm.CompleteAsync(turns, CancellationToken.None);
    var action = guard.Enforce(raw, profile, currentStage, fields, out var violation);

    string? finalViolation = violation;
    if (violation != null)
    {
        logger.LogWarning("[TURN DEBUG] Guard violation: {Violation}. Regenerating...", violation);
        turns.Add(new ChatTurn(ChatRole.Assistant, raw));
        turns.Add(new ChatTurn(ChatRole.System, $"CONSTRAINTS VIOLATED: {violation}. Please correct your response and follow the schema."));
        raw = await llm.CompleteAsync(turns, CancellationToken.None);
        action = guard.Enforce(raw, profile, currentStage, fields, out var secondViolation);
        finalViolation = secondViolation;
        if (secondViolation != null)
        {
            logger.LogError("[TURN DEBUG] Guard violation persisted: {Violation}. Using fallback.", secondViolation);
        }
    }

    logger.LogInformation("[TURN DEBUG] ModelIntent: {Intent}, NextStageChosen: {NextStage}", action.Intent, nextStep.NextStage);

    var assistantTurn = new CallTurn { Id = Guid.NewGuid(), TenantId = tenant.TenantId, CallId = call.Id, Role = "assistant", Text = action.Say ?? "" };
    db.CallTurns.Add(assistantTurn);

    // Update stage if it changed AND no final violation occurred
    if (finalViolation == null && nextStep.NextStage != currentStage)
    {
        logger.LogInformation("[TURN DEBUG] Advancing stage from {OldStage} to {NewStage}", currentStage, nextStep.NextStage);
        stateStore.SetCurrentStage(call, nextStep.NextStage);
    }

    if (action.Fields is not null && action.Fields.Count > 0)
    {
        var domainFields = fields.ToDictionary(k => k.Key, v => new DomainFieldValue { Value = v.Value.Value, Confirmed = v.Value.Confirmed });
        var updates = fieldPolicy.ProcessUpdates(call.CampaignCode, currentStage, domainFields, action.Fields);

        foreach (var up in updates)
        {
            if (up.Accepted)
            {
                var oldVal = fields.TryGetValue(up.FieldName, out var existing) ? existing.Value?.ToString() : null;
                var newVal = up.NewValue?.ToString();

                fields[up.FieldName] = new CallFieldValue
                {
                    Value = up.NewValue,
                    Confirmed = up.Confirmed,
                    Ts = DateTimeOffset.UtcNow
                };

                if (oldVal != newVal)
                {
                    db.CallFieldHistories.Add(new CallFieldHistory
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.TenantId,
                        CallId = call.Id,
                        FieldName = up.FieldName,
                        OldValue = oldVal,
                        NewValue = newVal,
                        Reason = up.Reason,
                        TurnId = userTurn.Id
                    });
                }

                // Sync with CallField table for redundant storage
                var existingRelational = await db.CallFields.FirstOrDefaultAsync(x => x.CallId == call.Id && x.Key == up.FieldName);
                if (existingRelational == null)
                {
                    db.CallFields.Add(new CallField { Id = Guid.NewGuid(), TenantId = tenant.TenantId, CallId = call.Id, Key = up.FieldName, Value = newVal ?? "" });
                }
                else
                {
                    existingRelational.Value = newVal ?? "";
                }
            }
        }
    }

    // Handle FinalConfirm logic
    var intent = (action.Intent ?? "").ToLowerInvariant();
    if (currentStage == CampaignStages.FinalConfirm)
    {
        // If LLM says intent is 'end' or similar, it means user confirmed
        if (intent == "end" || intent == "transfer" || intent == "set_callback")
        {
            foreach (var f in profile.RequiredFields)
            {
                if (fields.TryGetValue(f, out var val) && !val.Confirmed)
                {
                    val.Confirmed = true;
                    db.CallFieldHistories.Add(new CallFieldHistory
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.TenantId,
                        CallId = call.Id,
                        FieldName = f,
                        OldValue = val.Value?.ToString(),
                        NewValue = val.Value?.ToString(),
                        Reason = "final_confirmation",
                        TurnId = userTurn.Id
                    });
                }
            }
        }
    }

    // Special case for consent (treat as field)
    if (fields.TryGetValue("consent", out var consentVal) && consentVal.Value != null && !consentVal.Confirmed)
    {
        var valStr = consentVal.Value.ToString()?.ToLowerInvariant();
        if (valStr == "true" || valStr == "yes" || valStr == "confirmed")
        {
            consentVal.Confirmed = true;
            db.CallFieldHistories.Add(new CallFieldHistory
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                CallId = call.Id,
                FieldName = "consent",
                OldValue = "false",
                NewValue = "true",
                Reason = "consent_confirmed",
                TurnId = userTurn.Id
            });
        }
    }

    call.FieldsJson = JsonSerializer.Serialize(fields);

    if (intent == "dncl")
    {
        call.Status = CallStatus.Dnc;
        lead.Status = CallStatus.Dnc;
        call.EndedAt = DateTimeOffset.UtcNow;
        if (!await db.DoNotCalls.AnyAsync(x => x.TenantId == tenant.TenantId && x.Phone == lead.Phone))
            db.DoNotCalls.Add(new DoNotCall { Id = Guid.NewGuid(), TenantId = tenant.TenantId, Phone = lead.Phone, Reason = "Requested during call" });
    }
    else if (intent == "set_callback") { call.Status = CallStatus.CallbackScheduled; lead.Status = CallStatus.CallbackScheduled; }
    else if (intent == "transfer") { call.Status = CallStatus.Transferred; lead.Status = CallStatus.Transferred; }

    await db.SaveChangesAsync();
    return Results.Ok(action);
}).WithOpenApi();

app.MapPost("/v1/calls/{callId:guid}/status", async (Guid callId, [FromBody] UpdateStatusRequest req, TenantContext tenant, AppDbContext db) =>
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
    return Results.Ok(await db.Calls.Where(x => x.TenantId == tenant.TenantId).OrderByDescending(x => x.StartedAt).ToListAsync());
}).WithOpenApi();

// ========== DNC ==========
app.MapPost("/v1/dnc", async ([FromBody] AddDncRequest req, TenantContext tenant, AppDbContext db) =>
{
    var existing = await db.DoNotCalls.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Phone == req.Phone);
    if (existing is not null) return Results.Ok(existing);
    var dnc = new DoNotCall { Id = Guid.NewGuid(), TenantId = tenant.TenantId, Phone = req.Phone ?? "", Reason = req.Reason };
    db.DoNotCalls.Add(dnc);
    await db.SaveChangesAsync();
    return Results.Ok(dnc);
}).WithOpenApi();

app.MapGet("/v1/dnc", async (TenantContext tenant, AppDbContext db) =>
{
    return Results.Ok(await db.DoNotCalls.Where(x => x.TenantId == tenant.TenantId).OrderByDescending(x => x.CreatedAt).ToListAsync());
}).WithOpenApi();

app.MapPost("/v1/chat", async ([FromBody] ChatRequest req, ILlmProvider llm) =>
{
    var turns = new List<ChatTurn>();
    if (!string.IsNullOrWhiteSpace(req.System)) turns.Add(new ChatTurn(ChatRole.System, req.System!));
    foreach (var m in req.Messages ?? Array.Empty<ChatMessage>())
    {
        var role = (m.Role ?? "user").ToLowerInvariant() switch { "system" => ChatRole.System, "assistant" => ChatRole.Assistant, _ => ChatRole.User };
        turns.Add(new ChatTurn(role, m.Content ?? ""));
    }
    return Results.Ok(new { reply = await llm.CompleteAsync(turns, CancellationToken.None) });
}).WithOpenApi();

app.MapPost("/v1/tts", async ([FromBody] TtsRequest req, ITtsProvider tts, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "text is required" });
    using var ms = new MemoryStream();
    await foreach (var frame in tts.SynthesizeMuLawAsync(req.Text!, ct))
    {
        await ms.WriteAsync(frame.Data, ct);
        if (ms.Length >= (req.MaxBytes ?? 120_000)) break;
    }
    return Results.File(ms.ToArray(), "application/octet-stream", "tts.ulaw");
}).WithOpenApi();

app.MapPost("/v1/stt", async ([FromForm] SttFormRequest req, DeepgramPrerecordedStt stt, CancellationToken ct) =>
{
    if (req.File is null || req.File.Length == 0) return Results.BadRequest(new { error = "file is required" });
    await using var stream = req.File.OpenReadStream();
    var json = await stt.TranscribeAsync(stream, req.File.ContentType ?? "application/octet-stream", req.Query ?? "model=nova-2&smart_format=true&punctuate=true", ct);
    if (string.IsNullOrWhiteSpace(json)) return Results.Problem("Deepgram STT failed.");
    return Results.Text(json, "application/json");
}).DisableAntiforgery().WithOpenApi();

app.Run();

public sealed record CreateTenantRequest(string? Name);
public sealed record CreateAgentRequest(string? DisplayName, string? DefaultCampaignCode);
public sealed record CreateLeadRequest(string? CampaignCode, string? Name, string? Phone, string? State);
public sealed record StartCallRequest(Guid LeadId, Guid AgentId, CallDirection? Direction, string? CampaignCode, string? InboundUseCaseCode, string? PhoneFrom, string? PhoneTo, string? StartReason);
public sealed record NextRequest(string? Transcript, Dictionary<string, string>? Fields);
public sealed record AddDncRequest(string? Phone, string? Reason);
public sealed record UpdateStatusRequest(CallStatus Status, string? Notes, bool EndCall = true);
public sealed record ChatRequest(string? System, ChatMessage[]? Messages);
public sealed record ChatMessage(string? Role, string? Content);
public sealed record TtsRequest(string? Text, int? MaxBytes);
public sealed class SttFormRequest { [FromForm(Name = "file")] public IFormFile? File { get; set; } [FromForm(Name = "query")] public string? Query { get; set; } }

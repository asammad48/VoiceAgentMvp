using VoiceAgent.Domain.Models.Conversation;

namespace VoiceAgent.Host.Api.Storage;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Agent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = "Agent";
    public string? DefaultCampaignCode { get; set; } // FE/ACA/MEDICARE
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Lead
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string CampaignCode { get; set; } = "FE";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? State { get; set; }
    public CallStatus Status { get; set; } = CallStatus.New;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Call
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeadId { get; set; }
    public Guid AgentId { get; set; }
    public string CampaignCode { get; set; } = "FE";
    public CallStatus Status { get; set; } = CallStatus.Started;
    public string? Notes { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class CallTurn
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CallId { get; set; }
    public string Role { get; set; } = "user"; // user/assistant/system
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CallField
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CallId { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

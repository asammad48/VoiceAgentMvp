namespace VoiceAgent.Host.Api.Storage;

public enum CallStatus
{
    New = 0,
    Started = 1,
    Connected = 2,
    NotInterested = 3,
    CallbackScheduled = 4,
    NoAnswer = 5,
    Voicemail = 6,
    Dnc = 7,
    Transferred = 8,
    Completed = 9,
    Failed = 10,
    Busy = 11,
    FailedNetwork = 12,
    FailedProvider = 13,
    AgentHungUp = 14,
    LeadHungUp = 15,
    DncBlocked = 16
}

public enum CallDirection
{
    Inbound = 0,
    Outbound = 1
}

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
    public CallDirection Direction { get; set; } = CallDirection.Outbound;
    public string CampaignCode { get; set; } = "FE";
    public string? InboundUseCaseCode { get; set; }
    public string? PhoneFrom { get; set; }
    public string? PhoneTo { get; set; }
    public string? AsteriskChannelId { get; set; }
    public string? StartReason { get; set; } // console, vicidial, inbound, etc.

    public CallStatus Status { get; set; } = CallStatus.Started;
    public string? Notes { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }

    // Vicidial compatibility
    public string? ExternalSystem { get; set; }
    public string? ExternalCampaignId { get; set; }
    public string? ExternalLeadId { get; set; }
    public string? DispositionCode { get; set; }
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

public sealed class DoNotCall
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Phone { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

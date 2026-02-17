namespace VoiceAgent.Domain.Models.Api;

public enum CallStatusDto
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

public sealed record CallDto(
    Guid Id,
    Guid TenantId,
    Guid LeadId,
    Guid AgentId,
    string CampaignCode,
    string? InboundUseCaseCode,
    string? PhoneTo,
    string? PhoneFrom,
    string LeadName,
    string AgentName,
    string? IntroPitch
);

public sealed record AgentActionDto(
    string? Say,
    string? Intent,
    Dictionary<string, string>? Fields,
    string? NextStep
);

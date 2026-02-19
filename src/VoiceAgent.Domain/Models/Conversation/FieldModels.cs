namespace VoiceAgent.Domain.Models.Conversation;

public sealed class DomainFieldValue
{
    public object? Value { get; set; }
    public bool Confirmed { get; set; }
    public DateTimeOffset Ts { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FieldUpdateResult
{
    public string FieldName { get; set; } = "";
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public bool Accepted { get; set; }
    public bool Confirmed { get; set; }
    public bool Conflict { get; set; }
    public string? ClarificationQuestion { get; set; }
    public string Reason { get; set; } = "";
}

namespace VoiceAgent.Domain.Models.Conversation;

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
    Failed = 10
}

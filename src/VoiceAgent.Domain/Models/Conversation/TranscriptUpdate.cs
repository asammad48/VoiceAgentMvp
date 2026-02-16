namespace VoiceAgent.Domain.Models.Conversation;

public sealed record TranscriptUpdate(string Text, bool IsFinal, double? Confidence = null);

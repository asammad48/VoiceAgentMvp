namespace VoiceAgent.Domain.Models.Conversation;

public enum ChatRole { System, User, Assistant }
public sealed record ChatTurn(ChatRole Role, string Content);

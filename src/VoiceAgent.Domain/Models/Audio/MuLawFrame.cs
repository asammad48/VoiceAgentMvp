namespace VoiceAgent.Domain.Models.Audio;

/// <summary>μ-law (G.711 PCMU) audio frame. Typically 8000 Hz mono, 20ms = 160 bytes.</summary>
public sealed record MuLawFrame(byte[] Data, int SampleRate, int Channels, long TimestampMs);

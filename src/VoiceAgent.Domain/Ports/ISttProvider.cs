using VoiceAgent.Domain.Models.Audio;
using VoiceAgent.Domain.Models.Conversation;

namespace VoiceAgent.Domain.Ports;

public interface ISttProvider : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);
    ValueTask SendAudioAsync(MuLawFrame frame, CancellationToken ct);
    IAsyncEnumerable<TranscriptUpdate> GetUpdatesAsync(CancellationToken ct);
    ValueTask FlushAsync(CancellationToken ct);
}

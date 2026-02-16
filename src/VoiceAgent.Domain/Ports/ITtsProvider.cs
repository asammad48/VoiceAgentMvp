using VoiceAgent.Domain.Models.Audio;

namespace VoiceAgent.Domain.Ports;

public interface ITtsProvider
{
    IAsyncEnumerable<MuLawFrame> SynthesizeMuLawAsync(string text, CancellationToken ct);
}

using VoiceAgent.Domain.Models.Audio;

namespace VoiceAgent.Domain.Ports;

public interface IVadDetector
{
    bool IsSpeech(MuLawFrame frame);
}

using VoiceAgent.Domain.Models.Audio;
using VoiceAgent.Domain.Ports;

namespace VoiceAgent.Application.Vad;

public sealed class SimpleEnergyVad : IVadDetector
{
    private readonly int _threshold;
    private readonly int _minActiveCount;
    private int _activeCount;

    public SimpleEnergyVad(int threshold = 20, int minActiveCount = 4)
    {
        _threshold = threshold;
        _minActiveCount = minActiveCount;
    }

    public bool IsSpeech(MuLawFrame frame)
    {
        int sum = 0;
        var data = frame.Data;
        for (int i = 0; i < data.Length; i += 8)
            sum += Math.Abs(data[i] - 128);

        var avg = sum / Math.Max(1, data.Length / 8);
        if (avg > _threshold) _activeCount++;
        else _activeCount = Math.Max(0, _activeCount - 1);

        return _activeCount >= _minActiveCount;
    }
}

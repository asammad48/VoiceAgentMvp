namespace VoiceAgent.Domain.Ports;

public interface ITelephonyControl
{
    Task RunAsync(CancellationToken ct);
}

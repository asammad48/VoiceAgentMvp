using VoiceAgent.Domain.Models.Api;

namespace VoiceAgent.Domain.Ports;

public interface ITelephonyControl
{
    Task RunAsync(CancellationToken ct);
    Task TriggerOutboundAsync(CallDto call, CancellationToken ct);
}

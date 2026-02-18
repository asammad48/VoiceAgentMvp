using VoiceAgent.Application.Orchestration;
using VoiceAgent.Application.Vad;
using VoiceAgent.Domain.Ports;
using VoiceAgent.Infrastructure.Asterisk;
using VoiceAgent.Infrastructure.Media;
using VoiceAgent.Infrastructure.Providers.Deepgram;
using VoiceAgent.Infrastructure.Providers.ElevenLabs;
using VoiceAgent.Infrastructure.Providers.Llm;
using VoiceAgent.Infrastructure.Telephony;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;

        services.AddHttpClient<AriClient>();
        services.AddSingleton(sp =>
        {
            var log = sp.GetRequiredService<ILogger<AriClient>>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AriClient));
            return new AriClient(log, http, new Uri(cfg["Asterisk:BaseUrl"]!), cfg["Asterisk:AriUser"]!, cfg["Asterisk:AriPassword"]!);
        });

        services.AddSingleton<ISttProvider>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<DeepgramSttProvider>>();
            return new DeepgramSttProvider(log, cfg["Deepgram:ApiKey"]!, new Uri(cfg["Deepgram:WsUrl"]!));
        });

        services.AddHttpClient("hf");
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<HfRouterLlmProvider>>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("hf");
            return new HfRouterLlmProvider(log, http, new Uri(cfg["HfRouter:Endpoint"]!), cfg["HfRouter:Token"]!, cfg["HfRouter:Model"]!);
        });

        services.AddHttpClient("xi");
        services.AddSingleton<ITtsProvider>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<ElevenLabsTtsProvider>>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("xi");
            return new ElevenLabsTtsProvider(log, http, cfg["ElevenLabs:ApiKey"]!, cfg["ElevenLabs:VoiceId"]!, cfg["ElevenLabs:ModelId"]!);
        });

        services.AddSingleton<IVadDetector>(_ => new SimpleEnergyVad());

        services.AddSingleton<Func<int, IAudioTransport>>(sp => port =>
        {
            var log = sp.GetRequiredService<ILogger<RtpAudioTransport>>();
            return new RtpAudioTransport(log, port);
        });

        services.AddHttpClient<IVoiceAgentApiClient, VoiceAgentApiClient>(http =>
        {
            http.BaseAddress = new Uri(cfg["Api:BaseUrl"]!);
        });

        services.AddSingleton<Func<IAudioTransport, ConversationOrchestrator>>(sp => audio =>
        {
            var log = sp.GetRequiredService<ILogger<ConversationOrchestrator>>();
            return new ConversationOrchestrator(
                log,
                audio,
                sp.GetRequiredService<ISttProvider>(),
                sp.GetRequiredService<ITtsProvider>(),
                sp.GetRequiredService<IVadDetector>(),
                sp.GetRequiredService<IVoiceAgentApiClient>());
        });

        services.AddSingleton<ITelephonyControl>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<AsteriskAriTelephonyControl>>();
            var ari = sp.GetRequiredService<AriClient>();
            var audioFactory = sp.GetRequiredService<Func<int, IAudioTransport>>();
            var orchFactory = sp.GetRequiredService<Func<IAudioTransport, ConversationOrchestrator>>();
            var api = sp.GetRequiredService<IVoiceAgentApiClient>();
            var appName = cfg["Asterisk:StasisApp"]!;
            var ip = cfg["Media:WindowsListenIp"]!;
            var port = int.Parse(cfg["Media:WindowsListenPort"]!);
            var outboundEnabled = bool.TryParse(cfg["Outbound:Enabled"], out var b) && b;
            var outboundEndpoint = outboundEnabled ? cfg["Outbound:Endpoint"] : null;
            var callerId = outboundEnabled ? cfg["Outbound:CallerId"] : null;

            var tidStr = cfg["Outbound:TenantId"];
            Guid.TryParse(tidStr, out var tid);
            var aidStr = cfg["Outbound:DefaultAgentId"];
            Guid.TryParse(aidStr, out var aid);

            return new AsteriskAriTelephonyControl(log, ari, audioFactory, orchFactory, api, appName, ip, port, outboundEndpoint, callerId, tid, aid);
        });

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private readonly ITelephonyControl _telephony;
    private readonly IVoiceAgentApiClient _api;
    private readonly IConfiguration _cfg;

    public Worker(ILogger<Worker> log, ITelephonyControl telephony, IVoiceAgentApiClient api, IConfiguration cfg)
    {
        _log = log;
        _telephony = telephony;
        _api = api;
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Worker started.");

        var tenantIdStr = _cfg["Outbound:TenantId"];
        Guid.TryParse(tenantIdStr, out var tenantId);

        _ = Task.Run(() => _telephony.RunAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (tenantId != Guid.Empty)
                {
                    var call = await _api.ClaimNextCallAsync(tenantId, stoppingToken);
                    if (call != null)
                    {
                        await _telephony.TriggerOutboundAsync(call, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in worker polling loop");
            }

            await Task.Delay(200000, stoppingToken);
        }
    }
}

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

        services.AddHttpClient<IVoiceAgentApiClient, VoiceAgentApiClient>((sp, client) =>
        {
            var apiBase = cfg["Api:BaseUrl"] ?? "http://localhost:5000";
            client.BaseAddress = new Uri(apiBase);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler());

        services.AddSingleton<IVoiceAgentApiClient>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<VoiceAgentApiClient>>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(IVoiceAgentApiClient).FullName!);
            return new VoiceAgentApiClient(log, http, cfg["Inbound:TenantId"] ?? Guid.Empty.ToString());
        });

        services.AddSingleton<Func<int, IAudioTransport>>(sp => port =>
        {
            var log = sp.GetRequiredService<ILogger<RtpAudioTransport>>();
            return new RtpAudioTransport(log, port);
        });

        services.AddSingleton<Func<IAudioTransport, ConversationOrchestrator>>(sp => audio =>
        {
            var log = sp.GetRequiredService<ILogger<ConversationOrchestrator>>();
            return new ConversationOrchestrator(
                log,
                audio,
                sp.GetRequiredService<ISttProvider>(),
                sp.GetRequiredService<IVoiceAgentApiClient>(),
                sp.GetRequiredService<ITtsProvider>(),
                sp.GetRequiredService<IVadDetector>());
        });

        services.AddSingleton<ITelephonyControl>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<AsteriskAriTelephonyControl>>();
            var ari = sp.GetRequiredService<AriClient>();
            var api = sp.GetRequiredService<IVoiceAgentApiClient>();
            var audioFactory = sp.GetRequiredService<Func<int, IAudioTransport>>();
            var orchFactory = sp.GetRequiredService<Func<IAudioTransport, ConversationOrchestrator>>();
            var appName = cfg["Asterisk:StasisApp"]!;
            var ip = cfg["Media:WindowsListenIp"]!;
            var port = int.Parse(cfg["Media:WindowsListenPort"]!);
            var outboundEnabled = bool.TryParse(cfg["Outbound:Enabled"], out var b) && b;
            var outboundEndpoint = outboundEnabled ? cfg["Outbound:Endpoint"] : null;
            var callerId = outboundEnabled ? cfg["Outbound:CallerId"] : null;

            var inboundCfg = cfg.GetSection("Inbound");
            var defaultCampaign = inboundCfg["DefaultCampaign"] ?? "FE";
            var defaultAgentId = Guid.TryParse(inboundCfg["DefaultAgentId"], out var agId) ? agId : (Guid?)null;
            var tenantId = Guid.TryParse(inboundCfg["TenantId"], out var tid) ? tid : (Guid?)null;
            var campaignByDid = inboundCfg.GetSection("CampaignByDid").Get<Dictionary<string, string>>() ?? new();

            return new AsteriskAriTelephonyControl(
                log, ari, api, audioFactory, orchFactory, appName, ip, port,
                outboundEndpoint, callerId, defaultCampaign, defaultAgentId, tenantId, campaignByDid);
        });

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private readonly ITelephonyControl _telephony;
    public Worker(ILogger<Worker> log, ITelephonyControl telephony) { _log = log; _telephony = telephony; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Worker started.");
        await _telephony.RunAsync(stoppingToken);
    }
}

using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VoiceAgent.Infrastructure.Asterisk;

public sealed class AriClient : IAsyncDisposable
{
    private readonly ILogger<AriClient> _log;
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _user;
    private readonly string _pass;
    private ClientWebSocket? _ws;

    public AriClient(ILogger<AriClient> log, HttpClient http, Uri baseUri, string user, string pass)
    {
        _log = log;
        _http = http;
        _baseUri = baseUri;
        _user = user;
        _pass = pass;

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_user}:{_pass}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
    }

    public async Task ConnectEventsAsync(string appName, CancellationToken ct)
    {
        _ws = new ClientWebSocket();

        var wsScheme = _baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";

        var wsUri = new Uri(
            $"{wsScheme}://{_baseUri.Host}:{_baseUri.Port}/ari/events" +
            $"?api_key={Uri.EscapeDataString(_user)}:{Uri.EscapeDataString(_pass)}" +
            $"&app={Uri.EscapeDataString(appName)}"
        );

        _log.LogInformation("Connecting ARI WS: {Uri}", wsUri);
        await _ws.ConnectAsync(wsUri, ct);
        _log.LogInformation("ARI WS connected.");
    }


    public async IAsyncEnumerable<AriEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (_ws is null) throw new InvalidOperationException("WS not connected.");
        var buf = new byte[64 * 1024];
        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult res;
            do
            {
                res = await _ws.ReceiveAsync(buf, ct);
                if (res.MessageType == WebSocketMessageType.Close) yield break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            } while (!res.EndOfMessage);

            AriEvent? ev = null;
            try { ev = JsonSerializer.Deserialize<AriEvent>(sb.ToString()); }
            catch (Exception ex) { _log.LogWarning(ex, "Bad ARI event JSON"); }

            if (ev is not null) yield return ev;
        }
    }

    private string Url(string path) => $"{_baseUri.Scheme}://{_baseUri.Host}:{_baseUri.Port}{path}";

    public Task AnswerAsync(string channelId, CancellationToken ct)
        => _http.PostAsync(Url($"/ari/channels/{Uri.EscapeDataString(channelId)}/answer"), null, ct);

    public async Task<string> CreateBridgeAsync(string type, CancellationToken ct)
    {
        var resp = await _http.PostAsync(Url($"/ari/bridges?type={Uri.EscapeDataString(type)}"), null, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Bridge id missing");
    }

    public Task AddChannelToBridgeAsync(string bridgeId, string channelId, CancellationToken ct)
        => _http.PostAsync(Url($"/ari/bridges/{Uri.EscapeDataString(bridgeId)}/addChannel?channel={Uri.EscapeDataString(channelId)}"), null, ct);

    public async Task<string> CreateExternalMediaAsync(string appName, string externalHost, string format, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["app"] = appName,
            ["external_host"] = externalHost,
            ["format"] = format,
            ["encapsulation"] = "rtp",
            ["transport"] = "udp",
            ["direction"] = "both",
            ["connection_type"] = "client"
        });

        var resp = await _http.PostAsync(
            Url("/ari/channels/externalMedia"),
            new StringContent(payload, Encoding.UTF8, "application/json"),
            ct);

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"ARI externalMedia failed {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("ExternalMedia id missing");
    }


    public async Task<string> OriginateAsync(string endpoint, string appName, string? callerId, CancellationToken ct)
    {
        var qs = new List<string> { $"endpoint={Uri.EscapeDataString(endpoint)}", $"app={Uri.EscapeDataString(appName)}" };
        if (!string.IsNullOrWhiteSpace(callerId)) qs.Add($"callerId={Uri.EscapeDataString(callerId)}");
        var resp = await _http.PostAsync(Url($"/ari/channels?{string.Join("&", qs)}"), null, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Channel id missing");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws is not null && _ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch(Exception ex) {
           Console.WriteLine(ex);
        }
        _ws?.Dispose();
    }
}

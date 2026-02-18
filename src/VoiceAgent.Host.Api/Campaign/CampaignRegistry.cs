using System.Text.Json;

namespace VoiceAgent.Host.Api.Campaign;

public sealed class CampaignRegistry
{
    private readonly Dictionary<string, CampaignProfile> _profiles;

    public CampaignRegistry(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "CampaignProfiles.json");
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var dict = JsonSerializer.Deserialize<Dictionary<string, CampaignProfile>>(json, options)
                   ?? new();
        _profiles = dict.ToDictionary(k => k.Key.ToUpperInvariant(), v => v.Value);
    }

    public CampaignProfile Get(string code)
    {
        code = (code ?? "").ToUpperInvariant();
        if (_profiles.TryGetValue(code, out var p)) return p;
        throw new KeyNotFoundException($"Unknown campaign: {code}");
    }

    public IReadOnlyDictionary<string, CampaignProfile> All => _profiles;
}

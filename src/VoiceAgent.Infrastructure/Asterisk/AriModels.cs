using System.Text.Json.Serialization;

namespace VoiceAgent.Infrastructure.Asterisk;

public sealed class AriEvent
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("application")] public string? Application { get; set; }
    [JsonPropertyName("channel")] public AriChannel? Channel { get; set; }
}

public sealed class AriChannel
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("channeltype")] public string? Channeltype { get; set; }
}

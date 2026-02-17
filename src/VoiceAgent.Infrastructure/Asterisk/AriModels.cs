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
    [JsonPropertyName("dialplan")] public AriDialplan? Dialplan { get; set; }
    [JsonPropertyName("caller")] public AriCaller? Caller { get; set; }
}

public sealed class AriDialplan
{
    [JsonPropertyName("context")] public string? Context { get; set; }
    [JsonPropertyName("exten")] public string? Exten { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
}

public sealed class AriCaller
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
}

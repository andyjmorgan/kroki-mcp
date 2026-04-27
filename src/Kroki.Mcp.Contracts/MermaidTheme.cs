using System.Text.Json.Serialization;

namespace Kroki.Mcp.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<MermaidTheme>))]
public enum MermaidTheme
{
    [JsonStringEnumMemberName("default")]
    Default,

    [JsonStringEnumMemberName("dark")]
    Dark,

    [JsonStringEnumMemberName("forest")]
    Forest,

    [JsonStringEnumMemberName("neutral")]
    Neutral,

    [JsonStringEnumMemberName("base")]
    Base,
}

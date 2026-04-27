using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kroki.Mcp.Contracts;

[JsonConverter(typeof(MermaidThemeJsonConverter))]
public enum MermaidTheme
{
    Default,
    Dark,
    Forest,
    Neutral,
    Base,
}

public sealed class MermaidThemeJsonConverter : JsonStringEnumConverter<MermaidTheme>
{
    public MermaidThemeJsonConverter()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}

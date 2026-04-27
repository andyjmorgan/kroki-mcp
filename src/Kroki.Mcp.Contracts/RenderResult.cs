namespace Kroki.Mcp.Contracts;

public sealed record RenderResult(string Url, string Format, long SizeBytes, DateTimeOffset ExpiresAt);

namespace Kroki.Mcp.Contracts;

public interface IKrokiClient
{
    Task<byte[]> RenderMermaidAsync(string source, RenderFormat format, CancellationToken ct);
}

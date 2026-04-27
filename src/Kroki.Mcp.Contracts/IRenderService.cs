namespace Kroki.Mcp.Contracts;

public interface IRenderService
{
    Task<RenderResult> RenderMermaidAsync(string source, RenderFormat format, MermaidTheme? theme, CancellationToken ct);
}

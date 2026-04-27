using System.ComponentModel;
using Kroki.Mcp.Contracts;
using ModelContextProtocol.Server;

namespace Kroki.Mcp.Server.Tools;

[McpServerToolType]
public sealed class MermaidTools
{
    private readonly IRenderService _renderService;

    public MermaidTools(IRenderService renderService)
    {
        _renderService = renderService;
    }

    [McpServerTool(Name = "mermaid_render", Title = "Render Mermaid Diagram")]
    [Description("Render a Mermaid diagram to an image and return a public URL. The image is hosted for 30 days, then auto-deleted. Useful for embedding diagrams in chats, PRs, or docs without committing image files.")]
    public async Task<RenderResult> RenderMermaidAsync(
        [Description("Mermaid diagram source (e.g. 'flowchart LR; A-->B').")] string source,
        [Description("Output format: 'png' (default, raster, embeds anywhere) or 'svg' (vector, sharp at any scale).")] string? format = null,
        CancellationToken ct = default)
    {
        var renderFormat = ParseFormat(format);
        return await _renderService.RenderMermaidAsync(source, renderFormat, ct).ConfigureAwait(false);
    }

    private static RenderFormat ParseFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return RenderFormat.Png;
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "png" => RenderFormat.Png,
            "svg" => RenderFormat.Svg,
            _ => throw new ArgumentException($"Unsupported format '{format}'. Use 'png' or 'svg'.", nameof(format)),
        };
    }
}

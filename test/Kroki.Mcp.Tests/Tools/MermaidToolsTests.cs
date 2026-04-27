using Kroki.Mcp.Contracts;
using Kroki.Mcp.Server.Tools;
using NSubstitute;
using Xunit;

namespace Kroki.Mcp.Tests.Tools;

public class MermaidToolsTests
{
    [Theory]
    [InlineData(null, RenderFormat.Png)]
    [InlineData("", RenderFormat.Png)]
    [InlineData("png", RenderFormat.Png)]
    [InlineData("PNG", RenderFormat.Png)]
    [InlineData(" svg ", RenderFormat.Svg)]
    public async Task Format_string_parses_to_expected_enum(string? input, RenderFormat expected)
    {
        var renderer = Substitute.For<IRenderService>();
        renderer.RenderMermaidAsync(Arg.Any<string>(), Arg.Any<RenderFormat>(), Arg.Any<MermaidTheme?>(), Arg.Any<CancellationToken>())
            .Returns(new RenderResult("https://x", expected.ToString().ToLowerInvariant(), 1, DateTimeOffset.UtcNow));

        var tools = new MermaidTools(renderer);
        await tools.RenderMermaidAsync("flowchart LR;A-->B", input, ct: CancellationToken.None);

        await renderer.Received(1).RenderMermaidAsync("flowchart LR;A-->B", expected, Arg.Any<MermaidTheme?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Theme_passes_through_to_render_service()
    {
        var renderer = Substitute.For<IRenderService>();
        renderer.RenderMermaidAsync(Arg.Any<string>(), Arg.Any<RenderFormat>(), Arg.Any<MermaidTheme?>(), Arg.Any<CancellationToken>())
            .Returns(new RenderResult("https://x", "png", 1, DateTimeOffset.UtcNow));

        var tools = new MermaidTools(renderer);
        await tools.RenderMermaidAsync("flowchart LR;A-->B", format: "png", theme: MermaidTheme.Dark, CancellationToken.None);

        await renderer.Received(1).RenderMermaidAsync("flowchart LR;A-->B", RenderFormat.Png, MermaidTheme.Dark, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_format_throws_ArgumentException()
    {
        var tools = new MermaidTools(Substitute.For<IRenderService>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tools.RenderMermaidAsync("graph TD;A", "jpeg", ct: CancellationToken.None));
    }
}

using System.Diagnostics.Metrics;
using Kroki.Mcp.Contracts;
using Kroki.Mcp.Core.Kroki;
using Kroki.Mcp.Core.Rendering;
using Kroki.Mcp.Core.Storage;
using Kroki.Mcp.Core.Telemetry;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Kroki.Mcp.Tests.Rendering;

public class RenderServiceTests
{
    [Fact]
    public async Task Empty_source_throws_ArgumentException()
    {
        var service = BuildService(out _, out _);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenderMermaidAsync("   ", RenderFormat.Png, CancellationToken.None));
    }

    [Fact]
    public async Task Source_above_byte_cap_throws_ArgumentException()
    {
        var service = BuildService(out _, out _, maxBytes: 10);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RenderMermaidAsync(new string('x', 50), RenderFormat.Png, CancellationToken.None));
    }

    [Fact]
    public async Task Happy_path_uploads_with_30_day_ttl_and_returns_blob_url()
    {
        var service = BuildService(out var kroki, out var blob, retentionDays: 30);
        var rendered = new byte[] { 1, 2, 3 };
        kroki.RenderMermaidAsync("flowchart LR; A-->B", RenderFormat.Png, Arg.Any<CancellationToken>())
            .Returns(rendered);
        blob.PutAsync(Arg.Any<string>(), rendered, "image/png", TimeSpan.FromDays(30), Arg.Any<CancellationToken>())
            .Returns("https://example/diagrams/key.png");

        var result = await service.RenderMermaidAsync("flowchart LR; A-->B", RenderFormat.Png, CancellationToken.None);

        Assert.Equal("https://example/diagrams/key.png", result.Url);
        Assert.Equal("png", result.Format);
        Assert.Equal(rendered.LongLength, result.SizeBytes);
        await blob.Received(1).PutAsync(
            Arg.Is<string>(k => k.EndsWith(".png", StringComparison.Ordinal)),
            rendered,
            "image/png",
            TimeSpan.FromDays(30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Svg_format_uploads_with_svg_content_type_and_extension()
    {
        var service = BuildService(out var kroki, out var blob);
        var rendered = new byte[] { 9 };
        kroki.RenderMermaidAsync(Arg.Any<string>(), RenderFormat.Svg, Arg.Any<CancellationToken>())
            .Returns(rendered);
        blob.PutAsync(Arg.Any<string>(), Arg.Any<byte[]>(), "image/svg+xml", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns("https://example/diagrams/key.svg");

        var result = await service.RenderMermaidAsync("graph TD; X", RenderFormat.Svg, CancellationToken.None);

        Assert.Equal("svg", result.Format);
        await blob.Received(1).PutAsync(
            Arg.Is<string>(k => k.EndsWith(".svg", StringComparison.Ordinal)),
            rendered,
            "image/svg+xml",
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Kroki_failure_propagates_and_does_not_call_blob_store()
    {
        var service = BuildService(out var kroki, out var blob);
        kroki.RenderMermaidAsync(Arg.Any<string>(), Arg.Any<RenderFormat>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new KrokiRenderException(System.Net.HttpStatusCode.BadRequest, "syntax error"));

        await Assert.ThrowsAsync<KrokiRenderException>(() =>
            service.RenderMermaidAsync("garbage", RenderFormat.Png, CancellationToken.None));

        await blob.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default, default);
    }

    private static RenderService BuildService(
        out IKrokiClient kroki,
        out IBlobStore blob,
        int retentionDays = 30,
        int maxBytes = 1024 * 1024)
    {
        kroki = Substitute.For<IKrokiClient>();
        blob = Substitute.For<IBlobStore>();
        var krokiOptions = Options.Create(new KrokiOptions { MaxSourceBytes = maxBytes });
        var blobOptions = Options.Create(new BlobStoreOptions { RetentionDays = retentionDays });
        var metrics = new KrokiMcpMetrics(new TestMeterFactory());
        return new RenderService(kroki, blob, krokiOptions, blobOptions, TimeProvider.System, metrics);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }
}

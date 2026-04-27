using System.Net;
using System.Text;
using Kroki.Mcp.Contracts;
using Kroki.Mcp.Core.Kroki;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kroki.Mcp.Tests.Kroki;

public class KrokiClientTests
{
    [Theory]
    [InlineData(RenderFormat.Png, "mermaid/png")]
    [InlineData(RenderFormat.Svg, "mermaid/svg")]
    public async Task Posts_source_to_format_specific_path(RenderFormat format, string expectedPath)
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHandler(async req =>
        {
            capturedRequest = req;
            var bytes = await req.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://kroki.local/") };

        var client = new KrokiClient(http, NullLogger<KrokiClient>.Instance);
        var result = await client.RenderMermaidAsync("flowchart LR;A-->B", format, CancellationToken.None);

        Assert.Equal("flowchart LR;A-->B", Encoding.UTF8.GetString(result));
        Assert.NotNull(capturedRequest);
        Assert.EndsWith(expectedPath, capturedRequest!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_2xx_response_throws_KrokiRenderException()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("syntax error"),
        }));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://kroki.local/") };

        var client = new KrokiClient(http, NullLogger<KrokiClient>.Instance);

        var ex = await Assert.ThrowsAsync<KrokiRenderException>(() =>
            client.RenderMermaidAsync("bad", RenderFormat.Png, CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("syntax error", ex.ResponseBody, StringComparison.Ordinal);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}

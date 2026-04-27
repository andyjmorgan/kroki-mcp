using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kroki.Mcp.IntegrationTests;

[Trait("Category", "Integration")]
public class EndToEndRenderTests : IClassFixture<KrokiMcpStackFixture>, IAsyncLifetime
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly KrokiMcpStackFixture _stack;
    private McpClient? _client;

    public EndToEndRenderTests(KrokiMcpStackFixture stack) => _stack = stack;

    public async Task InitializeAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(new Uri(_stack.McpBaseUrl), "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, loggerFactory: null!);

        _client = await McpClient.CreateAsync(transport).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Mermaid_render_returns_a_url_pointing_at_a_real_PNG()
    {
        var result = await _client!.CallToolAsync(
            "mermaid_render",
            new Dictionary<string, object?>
            {
                ["source"] = "flowchart LR; A-->B; B-->C",
                ["format"] = "png",
            }).ConfigureAwait(false);

        Assert.False(result.IsError, "MCP tool reported an error result");

        var renderResult = ExtractRenderResult(result);
        Assert.Equal("png", renderResult.GetProperty("format").GetString());
        var url = renderResult.GetProperty("url").GetString();
        Assert.NotNull(url);
        Assert.StartsWith(_stack.SeaweedPublicBaseUrl, url);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        Assert.True(bytes.Length > 0, "PNG body was empty");
        Assert.True(bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic), "Response body did not start with the PNG magic header");
    }

    [Fact]
    public async Task Anonymous_directory_listing_is_disabled()
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync($"{_stack.SeaweedPublicBaseUrl}/diagrams/").ConfigureAwait(false);

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
            $"Expected 403/401/404 for anonymous bucket listing, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Tool_schema_advertises_theme_enum_values()
    {
        var tools = await _client!.ListToolsAsync().ConfigureAwait(false);
        var tool = tools.Single(t => t.Name == "mermaid_render");

        var properties = tool.JsonSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("theme", out var themeProperty), "Tool schema is missing the 'theme' parameter");

        var enumElement = ResolveEnum(themeProperty);
        var values = enumElement.EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(new[] { "default", "dark", "forest", "neutral", "base" }, values);
    }

    private static JsonElement ResolveEnum(JsonElement themeProperty)
    {
        if (themeProperty.TryGetProperty("enum", out var direct))
        {
            return direct;
        }

        // Nullable enums often appear as anyOf: [{enum: [...]}, {type: "null"}]
        if (themeProperty.TryGetProperty("anyOf", out var anyOf))
        {
            foreach (var branch in anyOf.EnumerateArray())
            {
                if (branch.TryGetProperty("enum", out var branchEnum))
                {
                    return branchEnum;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Theme schema did not expose enum values; saw {themeProperty.GetRawText()}");
    }

    [Fact]
    public async Task Invalid_mermaid_source_surfaces_an_error_result()
    {
        var result = await _client!.CallToolAsync(
            "mermaid_render",
            new Dictionary<string, object?>
            {
                ["source"] = "this is not mermaid syntax {{{",
                ["format"] = "png",
            }).ConfigureAwait(false);

        Assert.True(result.IsError, "Expected the tool to flag an error result for invalid mermaid source");
    }

    private static JsonElement ExtractRenderResult(CallToolResult result)
    {
        var content = result.Content.OfType<TextContentBlock>().FirstOrDefault()
            ?? throw new InvalidOperationException("Tool result had no text content");

        using var doc = JsonDocument.Parse(content.Text ?? string.Empty);
        return doc.RootElement.Clone();
    }
}

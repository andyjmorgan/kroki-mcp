using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Kroki.Mcp.Contracts;
using Microsoft.Extensions.Logging;

namespace Kroki.Mcp.Core.Kroki;

public sealed class KrokiClient : IKrokiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<KrokiClient> _logger;

    public KrokiClient(HttpClient http, ILogger<KrokiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<byte[]> RenderMermaidAsync(string source, RenderFormat format, CancellationToken ct)
    {
        var path = format switch
        {
            RenderFormat.Png => "mermaid/png",
            RenderFormat.Svg => "mermaid/svg",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(source));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Kroki returned {Status} for mermaid/{Format}: {Body}", response.StatusCode, format, body);
            throw new KrokiRenderException(response.StatusCode, body);
        }

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }
}

public sealed class KrokiRenderException : Exception
{
    public KrokiRenderException(HttpStatusCode status, string body)
        : base($"Kroki render failed ({(int)status}): {Truncate(body, 512)}")
    {
        StatusCode = status;
        ResponseBody = body;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

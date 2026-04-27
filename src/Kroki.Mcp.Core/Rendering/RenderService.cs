using System.Diagnostics;
using Kroki.Mcp.Contracts;
using Kroki.Mcp.Core.Kroki;
using Kroki.Mcp.Core.Storage;
using Kroki.Mcp.Core.Telemetry;
using Microsoft.Extensions.Options;

namespace Kroki.Mcp.Core.Rendering;

public sealed class RenderService : IRenderService
{
    private readonly IKrokiClient _kroki;
    private readonly IBlobStore _blobStore;
    private readonly KrokiOptions _krokiOptions;
    private readonly BlobStoreOptions _blobOptions;
    private readonly TimeProvider _time;
    private readonly KrokiMcpMetrics _metrics;

    public RenderService(
        IKrokiClient kroki,
        IBlobStore blobStore,
        IOptions<KrokiOptions> krokiOptions,
        IOptions<BlobStoreOptions> blobOptions,
        TimeProvider time,
        KrokiMcpMetrics metrics)
    {
        _kroki = kroki;
        _blobStore = blobStore;
        _krokiOptions = krokiOptions.Value;
        _blobOptions = blobOptions.Value;
        _time = time;
        _metrics = metrics;
    }

    public async Task<RenderResult> RenderMermaidAsync(string source, RenderFormat format, MermaidTheme? theme, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            _metrics.RenderRequests.Add(1, new KeyValuePair<string, object?>("format", format.ToString().ToLowerInvariant()), new KeyValuePair<string, object?>("status", "invalid"));
            throw new ArgumentException("Mermaid source is empty.", nameof(source));
        }

        var themedSource = ApplyTheme(source, theme);

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(themedSource);
        if (byteCount > _krokiOptions.MaxSourceBytes)
        {
            _metrics.RenderRequests.Add(1, new KeyValuePair<string, object?>("format", format.ToString().ToLowerInvariant()), new KeyValuePair<string, object?>("status", "too_large"));
            throw new ArgumentException(
                $"Mermaid source is {byteCount} bytes, exceeds limit of {_krokiOptions.MaxSourceBytes}.",
                nameof(source));
        }

        _metrics.RenderInputBytes.Record(byteCount, new KeyValuePair<string, object?>("format", format.ToString().ToLowerInvariant()));

        var (extension, contentType) = format switch
        {
            RenderFormat.Png => ("png", "image/png"),
            RenderFormat.Svg => ("svg", "image/svg+xml"),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        var formatTag = new KeyValuePair<string, object?>("format", extension);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var rendered = await _kroki.RenderMermaidAsync(themedSource, format, ct).ConfigureAwait(false);

            var now = _time.GetUtcNow();
            var ttl = TimeSpan.FromDays(_blobOptions.RetentionDays);
            var key = $"{now:yyyy/MM/dd}/{Guid.CreateVersion7()}.{extension}";

            var url = await _blobStore.PutAsync(key, rendered, contentType, ttl, ct).ConfigureAwait(false);

            stopwatch.Stop();
            _metrics.RenderDuration.Record(stopwatch.Elapsed.TotalMilliseconds, formatTag);
            _metrics.RenderOutputBytes.Record(rendered.LongLength, formatTag);
            _metrics.RenderRequests.Add(1, formatTag, new KeyValuePair<string, object?>("status", "ok"));

            return new RenderResult(url, extension, rendered.LongLength, now.Add(ttl));
        }
        catch (KrokiRenderException)
        {
            _metrics.RenderRequests.Add(1, formatTag, new KeyValuePair<string, object?>("status", "kroki_error"));
            throw;
        }
        catch (OperationCanceledException)
        {
            _metrics.RenderRequests.Add(1, formatTag, new KeyValuePair<string, object?>("status", "cancelled"));
            throw;
        }
        catch
        {
            _metrics.RenderRequests.Add(1, formatTag, new KeyValuePair<string, object?>("status", "error"));
            throw;
        }
    }

    // Prepends a Mermaid `%%{init: {'theme':'<theme>'}}%%` directive when a theme is requested
    // and the source doesn't already declare one — Mermaid only honours the first init block.
    private static string ApplyTheme(string source, MermaidTheme? theme)
    {
        if (theme is null or MermaidTheme.Default)
        {
            return source;
        }

        if (source.AsSpan().TrimStart().StartsWith("%%{init", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        var name = theme.Value.ToString().ToLowerInvariant();
        return $"%%{{init: {{'theme':'{name}'}}}}%%\n{source}";
    }
}

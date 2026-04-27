using System.Diagnostics.Metrics;

namespace Kroki.Mcp.Core.Telemetry;

public sealed class KrokiMcpMetrics : IDisposable
{
    public const string MeterName = "Kroki.Mcp";

    private readonly Meter _meter;

    public KrokiMcpMetrics(IMeterFactory factory)
    {
        _meter = factory.Create(MeterName);
        RenderRequests = _meter.CreateCounter<long>(
            "kroki_mcp.render.requests",
            unit: "{request}",
            description: "Number of mermaid render requests, tagged by format and status.");

        RenderDuration = _meter.CreateHistogram<double>(
            "kroki_mcp.render.duration",
            unit: "ms",
            description: "End-to-end render+upload duration in milliseconds, tagged by format.");

        RenderOutputBytes = _meter.CreateHistogram<long>(
            "kroki_mcp.render.output_bytes",
            unit: "By",
            description: "Size of rendered diagram in bytes, tagged by format.");

        RenderInputBytes = _meter.CreateHistogram<long>(
            "kroki_mcp.render.input_bytes",
            unit: "By",
            description: "Size of submitted mermaid source in bytes.");
    }

    public Counter<long> RenderRequests { get; }

    public Histogram<double> RenderDuration { get; }

    public Histogram<long> RenderOutputBytes { get; }

    public Histogram<long> RenderInputBytes { get; }

    public void Dispose() => _meter.Dispose();
}

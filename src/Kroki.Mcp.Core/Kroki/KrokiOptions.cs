namespace Kroki.Mcp.Core.Kroki;

public sealed class KrokiOptions
{
    public const string SectionName = "Kroki";

    public string DispatcherUrl { get; set; } = "http://kroki-dispatcher.kroki.svc.cluster.local:8000";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxSourceBytes { get; set; } = 256 * 1024;
}

namespace Kroki.Mcp.Core.Storage;

public sealed class BlobStoreOptions
{
    public const string SectionName = "BlobStore";

    public string Endpoint { get; set; } = "http://seaweedfs.kroki-mcp.svc.cluster.local:8333";
    public string Bucket { get; set; } = "diagrams";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = "https://kroki-cdn.donkeywork.dev";
    public int RetentionDays { get; set; } = 30;
}

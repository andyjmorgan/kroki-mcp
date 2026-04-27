namespace Kroki.Mcp.Contracts;

public interface IBlobStore
{
    Task<string> PutAsync(string key, byte[] body, string contentType, TimeSpan ttl, CancellationToken ct);
}

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Kroki.Mcp.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kroki.Mcp.Core.Storage;

public sealed class SeaweedFsBlobStore : IBlobStore, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly BlobStoreOptions _options;
    private readonly ILogger<SeaweedFsBlobStore> _logger;

    public SeaweedFsBlobStore(IOptions<BlobStoreOptions> options, ILogger<SeaweedFsBlobStore> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            UseHttp = _options.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
        };

        var creds = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        _s3 = new AmazonS3Client(creds, config);
    }

    public async Task<string> PutAsync(string key, byte[] body, string contentType, TimeSpan ttl, CancellationToken ct)
    {
        using var stream = new MemoryStream(body, writable: false);

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            DisablePayloadSigning = true,
        };

        // SeaweedFS native TTL — volume server expires on compaction.
        // Format: "<n>d", "<n>h", "<n>m". Header is preserved and acted on by the S3 gateway.
        var ttlValue = $"{(int)Math.Ceiling(ttl.TotalDays)}d";
        request.Headers["Seaweed-Ttl"] = ttlValue;

        await _s3.PutObjectAsync(request, ct).ConfigureAwait(false);

        var url = $"{_options.PublicBaseUrl.TrimEnd('/')}/{_options.Bucket}/{key}";
        _logger.LogInformation("Uploaded {Bytes}B to {Bucket}/{Key} ttl={Ttl}", body.Length, _options.Bucket, key, ttlValue);
        return url;
    }

    public void Dispose() => _s3.Dispose();
}

using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.Redis;
using Xunit;

namespace Kroki.Mcp.IntegrationTests;

/// <summary>
/// Spins the full container stack — Kroki dispatcher + mermaid sidecar, SeaweedFS in S3 mode, Redis —
/// and hosts kroki-mcp itself on Kestrel pointing at them. Exposes the public URL the MCP server is
/// listening on plus the SeaweedFS public S3 base, so tests can drive the MCP client and then HTTP-GET
/// the rendered diagram.
/// </summary>
public sealed class KrokiMcpStackFixture : IAsyncLifetime
{
    private const string SeaweedAccessKey = "kroki-mcp-writer";
    private const string SeaweedSecretKey = "test-secret-key";
    private const string IdentitiesJson = $$"""
{
  "identities": [
    { "name": "anonymous", "actions": ["Read:diagrams/*"] },
    {
      "name": "kroki-mcp-writer",
      "credentials": [{ "accessKey": "{{SeaweedAccessKey}}", "secretKey": "{{SeaweedSecretKey}}" }],
      "actions": ["Admin:diagrams/*", "Read:diagrams/*", "Write:diagrams/*", "List:diagrams/*"]
    }
  ]
}
""";

    private INetwork? _network;
    private IContainer? _mermaid;
    private IContainer? _kroki;
    private IContainer? _seaweed;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _factory;
    private readonly List<KeyValuePair<string, string?>> _appliedEnvVars = new();

    public string McpBaseUrl { get; private set; } = string.Empty;

    public string SeaweedPublicBaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync().ConfigureAwait(false);

        _mermaid = new ContainerBuilder()
            .WithImage("yuzutech/kroki-mermaid:latest")
            .WithNetwork(_network)
            .WithNetworkAliases("kroki-mermaid")
            .WithPortBinding(8002, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPath("/").ForPort(8002).ForStatusCode(HttpStatusCode.OK)))
            .Build();
        await _mermaid.StartAsync().ConfigureAwait(false);

        _kroki = new ContainerBuilder()
            .WithImage("yuzutech/kroki:latest")
            .WithNetwork(_network)
            .WithEnvironment("KROKI_MERMAID_HOST", "kroki-mermaid")
            .WithEnvironment("KROKI_MERMAID_PORT", "8002")
            .WithPortBinding(8000, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPath("/health").ForPort(8000).ForStatusCode(HttpStatusCode.OK)))
            .Build();
        await _kroki.StartAsync().ConfigureAwait(false);

        _seaweed = new ContainerBuilder()
            .WithImage("chrislusf/seaweedfs:3.80")
            .WithCommand("server", "-s3", "-s3.config=/etc/seaweedfs/identities.json", "-dir=/data", "-master.volumeSizeLimitMB=64")
            .WithResourceMapping(Encoding.UTF8.GetBytes(IdentitiesJson), "/etc/seaweedfs/identities.json")
            .WithPortBinding(8333, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8333))
            .Build();
        await _seaweed.StartAsync().ConfigureAwait(false);

        _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await _redis.StartAsync().ConfigureAwait(false);

        var krokiUrl = $"http://{_kroki.Hostname}:{_kroki.GetMappedPublicPort(8000)}";
        var seaweedUrl = $"http://{_seaweed.Hostname}:{_seaweed.GetMappedPublicPort(8333)}";
        SeaweedPublicBaseUrl = seaweedUrl;

        await EnsureBucketAsync(seaweedUrl).ConfigureAwait(false);

        // The host reads its config from environment variables; set them before the factory builds.
        SetEnv("Kroki__DispatcherUrl", krokiUrl);
        SetEnv("Kroki__Timeout", "00:00:30");
        SetEnv("Kroki__MaxSourceBytes", "262144");
        SetEnv("BlobStore__Endpoint", seaweedUrl);
        SetEnv("BlobStore__Bucket", "diagrams");
        SetEnv("BlobStore__AccessKey", SeaweedAccessKey);
        SetEnv("BlobStore__SecretKey", SeaweedSecretKey);
        SetEnv("BlobStore__PublicBaseUrl", seaweedUrl);
        SetEnv("BlobStore__RetentionDays", "30");
        SetEnv("Redis__ConnectionString", _redis.GetConnectionString());
        SetEnv("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        SetEnv("ASPNETCORE_ENVIRONMENT", "Test");

        _factory = new TestFactory();

        // Trigger host construction so we can read the bound Kestrel address.
        _ = _factory.Services;
        var server = _factory.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not bind a listen address.");
        McpBaseUrl = address;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var c in new IAsyncDisposable?[] { _redis, _seaweed, _kroki, _mermaid })
        {
            if (c is not null)
            {
                await c.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (_network is not null)
        {
            await _network.DisposeAsync().ConfigureAwait(false);
        }

        // Restore process-level env vars so parallel test classes don't see leftovers.
        foreach (var kvp in _appliedEnvVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }
    }

    private void SetEnv(string key, string? value)
    {
        _appliedEnvVars.Add(new(key, Environment.GetEnvironmentVariable(key)));
        Environment.SetEnvironmentVariable(key, value);
    }

    private static async Task EnsureBucketAsync(string seaweedUrl)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = seaweedUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            UseHttp = seaweedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
        };
        using var s3 = new AmazonS3Client(new BasicAWSCredentials(SeaweedAccessKey, SeaweedSecretKey), config);
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = "diagrams" }).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Idempotent.
        }
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.UseUrls("http://127.0.0.1:0");
            });

            return base.CreateHost(builder);
        }
    }
}

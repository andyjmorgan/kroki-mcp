using Kroki.Mcp.Contracts;
using Kroki.Mcp.Core.Kroki;
using Kroki.Mcp.Core.Rendering;
using Kroki.Mcp.Core.Storage;
using Kroki.Mcp.Core.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kroki.Mcp.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddKrokiMcpCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<KrokiOptions>()
            .Bind(configuration.GetSection(KrokiOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<BlobStoreOptions>()
            .Bind(configuration.GetSection(BlobStoreOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient<IKrokiClient, KrokiClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KrokiOptions>>().Value;
            client.BaseAddress = new Uri(opts.DispatcherUrl.TrimEnd('/') + "/");
            client.Timeout = opts.Timeout;
        });

        services.AddSingleton<IBlobStore, SeaweedFsBlobStore>();
        services.AddSingleton<KrokiMcpMetrics>();
        services.AddScoped<IRenderService, RenderService>();

        return services;
    }
}

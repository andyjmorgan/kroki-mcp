using Kroki.Mcp.Core;
using Kroki.Mcp.Core.Telemetry;
using Kroki.Mcp.Server.Tools;
using Microsoft.AspNetCore.DataProtection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

const string outputTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: outputTemplate)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: outputTemplate));

    builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

    builder.Services.AddKrokiMcpCore(builder.Configuration);
    builder.Services.AddHealthChecks();

    var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(redisConnectionString))
    {
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(redis, "KrokiMcp:DataProtectionKeys")
            .SetApplicationName("kroki-mcp");
    }

    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true)
        .WithTools<MermaidTools>();

    var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: "kroki-mcp",
            serviceVersion: serviceVersion,
            serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName))
        .WithTracing(b => b
            .AddSource("ModelContextProtocol")
            .AddSource(KrokiMcpMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation())
        .WithMetrics(b => b
            .AddMeter(KrokiMcpMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation())
        .UseOtlpExporter();

    var app = builder.Build();

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, _, ex) =>
        {
            if (ex is null && httpContext.Response.StatusCode < 400 &&
                httpContext.Request.Path.StartsWithSegments("/healthz"))
            {
                return LogEventLevel.Verbose;
            }

            return LogEventLevel.Information;
        };
    });

    app.MapHealthChecks("/healthz");
    app.MapMcp();

    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    Log.Fatal(ex, "kroki-mcp host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

using HVO.Enterprise.Telemetry;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Enterprise.Telemetry.OpenTelemetry;
using HVO.Enterprise.Telemetry.Serilog;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.Components;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Workers;
using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, _, loggerConfig) =>
{
    var logDir = Path.Combine(ctx.HostingEnvironment.ContentRootPath, "logs");
    Directory.CreateDirectory(logDir);

    loggerConfig
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("HVO.Gateway.SolarAssistant", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .Enrich.WithTelemetry()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            new CompactJsonFormatter(),
            Path.Combine(logDir, "solarassistant-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 100_000_000,
            rollOnFileSizeLimit: true);

    // Forward logs to the OTel collector sidecar when the endpoint is configured.
    var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "hvo-solarassistant";
        loggerConfig.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = otlpEndpoint.TrimEnd('/') + "/v1/logs";
            options.Protocol = OtlpProtocol.HttpProtobuf;
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = serviceName
            };
        });
    }
});

builder.Services
    .AddOptions<SolarAssistantOptions>()
    .BindConfiguration(SolarAssistantOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<OutboxOptions>()
    .BindConfiguration(OutboxOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

    // ── Telemetry ──────────────────────────────────────────────────────────────────────────────
    builder.Services.AddTelemetry(builder.Configuration.GetSection("Telemetry"));
    builder.Services.AddOpenTelemetryExport(options =>
    {
        options.EnableTraceExport = false;
        options.EnableMetricsExport = false;
        options.EnableLogExport = false;
        options.EnableStandardMeters = true;
        options.AdditionalMeterNames.Add("hvo.solarassistant");
        options.AdditionalActivitySources.Add("hvo.solarassistant");
    });
    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
    {
        builder.Services.AddOpenTelemetry()
            .WithTracing(tb => tb.AddOtlpExporter())
            .WithMetrics(mb => mb.AddOtlpExporter());
    }
    builder.Services.AddTelemetryStatistics();
    builder.Services.AddTelemetryHealthCheck();

    var outboxConfig = builder.Configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();
    var dbPath = !string.IsNullOrWhiteSpace(outboxConfig?.DbPath)
        ? outboxConfig.DbPath
        : Path.Combine(builder.Environment.ContentRootPath, "outbox.db");
    builder.Services.AddDbContext<OutboxDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
builder.Services.AddScoped<PowerOutboxWriter>();
builder.Services.AddScoped<PowerInventoryConfigurationWriter>();

builder.Services.AddHttpClient("SolarAssistantRest", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SolarAssistantOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});
builder.Services.AddHttpClient("PowerApi", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<ISolarAssistantClient, SolarAssistantRestClient>();
builder.Services.AddSingleton<SolarAssistantSnapshotWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SolarAssistantSnapshotWorker>());
builder.Services.AddSingleton<SolarAssistantMqttInventoryStore>();
builder.Services.AddSingleton<SolarAssistantMqttDiscoveryWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SolarAssistantMqttDiscoveryWorker>());
builder.Services.AddSingleton<PowerApiForwarder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PowerApiForwarder>());
builder.Services.AddSingleton<SolarAssistantGatewayHealthService>();
builder.Services.AddSingleton<IGatewayHealthSnapshotProvider>(sp => sp.GetRequiredService<SolarAssistantGatewayHealthService>());
builder.Services.AddSingleton<IGatewayStatusPayloadProvider>(sp => sp.GetRequiredService<SolarAssistantGatewayHealthService>());
builder.Services.AddSingleton<GatewayStatusSnapshotWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayStatusSnapshotWorker>());
    builder.Services.AddHealthChecks()
        .AddCheck<TelemetryHealthCheck>("telemetry")
        .AddCheck<SolarAssistantGatewayHealthCheck>("solarassistant-gateway");
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();
var exposeDiagnostics = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Diagnostics:ExposeDetailedEndpoints", false);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
        db,
        PowerOutboxPayloadTypes.PowerReading,
        PowerOutboxPayloadTypes.PowerReadingVersion);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("SolarAssistant gateway encountered an unexpected error.");
    }));
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapHealthChecks("/health");
if (exposeDiagnostics)
{
    app.MapGet("/status", (SolarAssistantSnapshotWorker snapshotWorker, PowerApiForwarder forwarder, SolarAssistantGatewayHealthService healthService) => new
    {
        snapshotWorker.LastSnapshotAt,
        snapshotWorker.LastMetricCount,
        snapshotWorker.LastError,
        outbox = new
        {
            forwarder.PendingCount,
            forwarder.FailedCount,
            forwarder.LastSentAt,
            forwarder.LastBatchCount,
            forwarder.LastError,
        },
        snapshot = snapshotWorker.LastSnapshot,
        health = healthService.GetSnapshot(),
    });
    app.MapGet("/inventory", (SolarAssistantSnapshotWorker snapshotWorker) => snapshotWorker.LastInventory is null
        ? Results.NotFound(new { message = "SolarAssistant metric inventory is not available yet." })
        : Results.Ok(snapshotWorker.LastInventory));
    app.MapGet("/mqtt-inventory", (SolarAssistantMqttDiscoveryWorker mqttWorker) => Results.Ok(mqttWorker.Inventory));
}
app.MapGet("/gateway-health", (HttpContext httpContext, SolarAssistantGatewayHealthService healthService, IOptions<OutboxOptions> outboxOptions) =>
{
    if (!SolarAssistantGatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(healthService.GetSnapshot());
});

await app.RunAsync();

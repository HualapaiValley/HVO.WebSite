using HVO.Enterprise.Telemetry;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Enterprise.Telemetry.OpenTelemetry;
using HVO.Enterprise.Telemetry.Serilog;
using HVO.Edge.Outbox;
using HVO.Edge.Contracts;
using HVO.Hardware.VictronSmartShunt.Components;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
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
        .MinimumLevel.Override("HVO.Hardware.VictronSmartShunt", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .Enrich.WithTelemetry()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            new CompactJsonFormatter(),
            Path.Combine(logDir, "smartshunt-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 100_000_000,
            rollOnFileSizeLimit: true);

    // Forward logs to the OTel collector sidecar when the endpoint is configured.
    var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "hvo-smartshunt";
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
    .AddOptions<SmartShuntOptions>()
    .BindConfiguration(SmartShuntOptions.SectionName)
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
        options.AdditionalMeterNames.Add("hvo.smartshunt");
        options.AdditionalActivitySources.Add("hvo.smartshunt");
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

    builder.Services.AddDbContext<OutboxDbContext>(o => o.UseSqlite($"Data Source={dbPath};Default Timeout=30", sqlite => sqlite.CommandTimeout(30)));
builder.Services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
builder.Services.AddSingleton<RuntimeOutboxSettings>();
builder.Services.AddScoped<PowerOutboxWriter>();

builder.Services.AddHttpClient("PowerApi", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(120);
}).AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
});

builder.Services.AddSingleton<SmartShuntPublicSession>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SmartShuntPublicSession>());
builder.Services.AddSingleton<ISmartShuntSessionState>(sp => sp.GetRequiredService<SmartShuntPublicSession>());
builder.Services.AddSingleton<ISmartShuntPrivateInfoSource, SmartShuntPrivateInfoSource>();
builder.Services.AddSingleton<SmartShuntWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SmartShuntWorker>());
builder.Services.AddSingleton<PowerApiForwarder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PowerApiForwarder>());
builder.Services.AddSingleton<SmartShuntGatewayHealthService>();
builder.Services.AddSingleton<ISmartShuntGatewayHealthSnapshotProvider>(sp => sp.GetRequiredService<SmartShuntGatewayHealthService>());

    builder.Services.AddHealthChecks()
        .AddCheck<TelemetryHealthCheck>("telemetry")
        .AddCheck<SmartShuntGatewayHealthCheck>("smartshunt-gateway");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();
var gatewayStartedAtUtc = DateTime.UtcNow;
var exposeDiagnostics = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Diagnostics:ExposeDetailedEndpoints", false);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
        db,
        SmartShuntOutboxPayloadTypes.Reading,
        SmartShuntOutboxPayloadTypes.ReadingVersion);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("SmartShunt gateway encountered an unexpected error.");
    }));
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});

if (exposeDiagnostics)
{
    app.MapGet("/status", (SmartShuntWorker worker, PowerApiForwarder forwarder, SmartShuntGatewayHealthService healthService) => new
    {
        worker.LastSnapshotAt,
        worker.LastError,
        worker.LastSnapshot,
        worker.PrivateInfo,
        outbox = new
        {
            forwarder.PendingCount,
            forwarder.FailedCount,
            forwarder.LastSentAt,
            forwarder.LastBatchCount,
            forwarder.LastError,
        },
        health = healthService.GetSnapshot(),
    });
}

app.MapGet("/gateway-health", (HttpContext httpContext, SmartShuntGatewayHealthService healthService, IOptions<OutboxOptions> outboxOptions) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(healthService.GetSnapshot());
});

app.MapGet("/diagnostics/health", async (HttpContext httpContext, SmartShuntWorker worker, SmartShuntGatewayHealthService healthService, OutboxDbContext db, IOptions<SmartShuntOptions> smartShuntOptions, IOptions<OutboxOptions> outboxOptions, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var status = await CreateSmartShuntDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, worker, healthService, db, smartShuntOptions.Value, cancellationToken);
    return Results.Ok(status.Health);
});

app.MapGet("/diagnostics/status", async (HttpContext httpContext, SmartShuntWorker worker, SmartShuntGatewayHealthService healthService, OutboxDbContext db, IOptions<SmartShuntOptions> smartShuntOptions, IOptions<OutboxOptions> outboxOptions, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    return Results.Ok(await CreateSmartShuntDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, worker, healthService, db, smartShuntOptions.Value, cancellationToken));
});

app.MapGet("/diagnostics/outbox", async (HttpContext httpContext, OutboxDbContext db, IOptions<OutboxOptions> outboxOptions, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    return Results.Ok(await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken));
});

app.MapPut("/diagnostics/outbox/settings", (HttpContext httpContext, RuntimeOutboxSettings runtimeSettings, IOptions<OutboxOptions> outboxOptions, OutboxSettingsUpdate? update) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    if (update is null)
        return Results.BadRequest("Request body is required.");

    if (update.Reset == true)
    {
        runtimeSettings.Reset();
        return Results.Ok(new OutboxSettingsResponse(
            runtimeSettings.BatchSizeOverride ?? outboxOptions.Value.BatchSize,
            runtimeSettings.SweepIntervalSecondsOverride ?? outboxOptions.Value.SweepIntervalSeconds,
            false));
    }

    try
    {
        runtimeSettings.BatchSizeOverride = update.BatchSize;
        runtimeSettings.SweepIntervalSecondsOverride = update.SweepIntervalSeconds;
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(ex.Message);
    }

    return Results.Ok(new OutboxSettingsResponse(
        runtimeSettings.BatchSizeOverride ?? outboxOptions.Value.BatchSize,
        runtimeSettings.SweepIntervalSecondsOverride ?? outboxOptions.Value.SweepIntervalSeconds,
        true));
});

await app.RunAsync();

static async Task<GatewayDiagnosticStatusResponse> CreateSmartShuntDiagnosticStatusAsync(
    DateTime startedAtUtc,
    string environmentName,
    SmartShuntWorker worker,
    SmartShuntGatewayHealthService healthService,
    OutboxDbContext db,
    SmartShuntOptions options,
    CancellationToken cancellationToken)
{
    var now = DateTime.UtcNow;
    var snapshot = healthService.GetSnapshot(now);
    var fresh = worker.LastSnapshotAt is { } lastSnapshotAt && now - lastSnapshotAt <= TimeSpan.FromSeconds(options.SampleStaleAfterSeconds);
    var health = new GatewayHealthSnapshot(
        MapHealthState(snapshot.State),
        snapshot.EvaluatedAtUtc,
        snapshot.Alerts.Select(alert => new GatewayHealthAlert(alert.Code, MapSeverity(alert.Severity), alert.Message)).ToArray(),
        ResolveSmartShuntFreshness(worker.LastSnapshotAt, worker.LastError, fresh),
        null,
        null);

    return new GatewayDiagnosticStatusResponse(
        "1.0",
        new GatewayIdentity("smartshunt", "Victron SmartShunt Gateway", GatewayDomain.Power, options.SourceId, options.DeviceId, Environment.MachineName),
        new GatewayRuntimeInfo(startedAtUtc, now, now - startedAtUtc, environmentName),
        health,
        GatewayDeviceCounts.SingleSource(fresh, health.State == GatewayHealthState.Warning),
        await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken),
        CreateTelemetryDiagnostics("hvo-smartshunt", "hvo.smartshunt"),
        new Dictionary<string, string>
        {
            ["health"] = "/diagnostics/health",
            ["status"] = "/diagnostics/status",
            ["outbox"] = "/diagnostics/outbox",
            ["legacyGatewayHealth"] = "/gateway-health",
            ["legacyStatus"] = "/status",
        });
}

static GatewaySampleState ResolveSmartShuntFreshness(DateTime? lastSnapshotAtUtc, string? lastError, bool fresh)
{
    if (!string.IsNullOrWhiteSpace(lastError))
        return GatewaySampleState.Error;
    if (lastSnapshotAtUtc is null)
        return GatewaySampleState.Waiting;
    return fresh ? GatewaySampleState.Live : GatewaySampleState.Stale;
}

static GatewayHealthState MapHealthState(string state) => state switch
{
    "healthy" => GatewayHealthState.Healthy,
    "warning" => GatewayHealthState.Warning,
    "critical" => GatewayHealthState.Critical,
    _ => GatewayHealthState.Unknown,
};

static GatewayAlertSeverity MapSeverity(SmartShuntGatewayHealthSeverity severity) => severity switch
{
    SmartShuntGatewayHealthSeverity.Critical => GatewayAlertSeverity.Critical,
    SmartShuntGatewayHealthSeverity.Warning => GatewayAlertSeverity.Warning,
    _ => GatewayAlertSeverity.Info,
};

static GatewayTelemetryDiagnostics CreateTelemetryDiagnostics(string defaultServiceName, string sourceName) => new(
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")),
    Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? defaultServiceName,
    [
        GatewayTelemetryConventions.MetricNames.OutboxDepth,
        GatewayTelemetryConventions.MetricNames.OutboxForwardSuccess,
        GatewayTelemetryConventions.MetricNames.OutboxForwardFailure,
        GatewayTelemetryConventions.MetricNames.DeviceFreshnessSeconds,
        GatewayTelemetryConventions.MetricNames.DevicePollFailure,
    ],
    [sourceName]);

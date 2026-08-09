using HVO.Edge.Outbox;
using HVO.Edge.Contracts;
using HVO.Enterprise.Telemetry;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Enterprise.Telemetry.Http;
using HVO.Enterprise.Telemetry.OpenTelemetry;
using HVO.Edge.Hosting.Logging;
using HVO.Edge.Hosting.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Components;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.History;
using HVO.Hardware.JkBms.Outbox.Forwarders;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Telemetry;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseHvoGatewayLogging(new GatewayLogIdentity(
    "hvo-jkbms",
    "jkbms",
    "battery-bms"));

// ── Options ────────────────────────────────────────────────────────────────────
builder.Services
    .AddOptions<JkBmsOptions>()
    .BindConfiguration(JkBmsOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<JkBmsDisplayTimeZoneResolver>();

builder.Services
    .AddOptions<OutboxOptions>()
    .BindConfiguration(OutboxOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Telemetry ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddTelemetry(builder.Configuration.GetSection("Telemetry"));
// HVO library sets the OTLP endpoint programmatically, which disables AppendSignalPathToEndpoint
// in OTel SDK 1.10+, causing exports to POST to the root URL (404). Disable HVO's built-in
// exporters and use native SDK exporters with no configure callback — the SDK reads
// OTEL_EXPORTER_OTLP_ENDPOINT and OTEL_EXPORTER_OTLP_PROTOCOL from environment and appends
// the correct signal paths (/v1/traces, /v1/metrics, /v1/logs).
builder.Services.AddOpenTelemetryExport(options =>
{
    options.EnableTraceExport = false;
    options.EnableMetricsExport = false;
    options.EnableLogExport = false;
    options.EnableStandardMeters = true;
    options.AdditionalMeterNames.Add("hvo.jkbms");
    options.AdditionalMeterNames.Add(GatewayTelemetryConventions.MeterName);
    options.AdditionalActivitySources.Add(GatewayTelemetryConventions.ActivitySourceName);
});
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddAttributes(GatewayTelemetryResource.Create(
            "hvo-jkbms", new GatewayTelemetryIdentity("jkbms", "battery-bms", "hvo"), builder.Environment.EnvironmentName)))
        .WithTracing(tb => tb.AddOtlpExporter())
        .WithMetrics(mb => mb.AddOtlpExporter());
}
builder.Services.AddSingleton<BmsTelemetry>();
builder.Services.AddTelemetryStatistics();
builder.Services.AddTelemetryHealthCheck();
builder.Services.AddHealthChecks()
    .AddCheck<TelemetryHealthCheck>("telemetry")
    .AddCheck<BmsDeviceHealthCheck>("bms-device");

// ── BLE transport ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IBluetoothAdapterCoordinator, BluetoothAdapterCoordinator>();
builder.Services.AddSingleton<IBmsTransportFactory, JkBmsBluetoothTransportFactory>();

// ── Alarm handler ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IBmsAlarmHandler, NullAlarmHandler>();

// ── SQLite outbox ──────────────────────────────────────────────────────────────
// NOTE: outboxConfig is deserialized here manually (before the DI container is built)
// solely to resolve the DB file path for AddDbContext. The options are also bound via
// AddOptions<OutboxOptions>() above, which applies DataAnnotations validation at startup.
// Do not consolidate these two reads — the DI-bound options are not available until after
// builder.Build(), which is too late to supply the connection string.
var outboxConfig = builder.Configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();
string dbPath = !string.IsNullOrWhiteSpace(outboxConfig?.DbPath)
    ? outboxConfig.DbPath
    : Path.Combine(builder.Environment.ContentRootPath, "outbox.db");
builder.Services.AddDbContext<OutboxDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath};Default Timeout=30", sqlite => sqlite.CommandTimeout(30)),
    ServiceLifetime.Scoped);
builder.Services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
builder.Services.AddSingleton<RuntimeOutboxSettings>();
builder.Services.AddScoped<BmsOutboxWriter>();
builder.Services.AddSingleton<IBmsHistoryService, BmsHistoryService>();

// ── HTTP client for outbox forwarder ────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("OutboxForwarder", (sp, client) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(120);
}).AddHttpMessageHandler(sp => new TelemetryHttpMessageHandler(
    new HttpInstrumentationOptions { CaptureRequestHeaders = false, CaptureResponseHeaders = false },
    sp.GetService<ILogger<TelemetryHttpMessageHandler>>()))
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
});

// ── Forwarders ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IReadingForwarder, HttpApiForwarder>();

// ── Background workers ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<BmsPollerWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BmsPollerWorker>());

builder.Services.AddSingleton<ForwarderCoordinator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ForwarderCoordinator>());

// ── Blazor Server ──────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();
var gatewayStartedAtUtc = DateTime.UtcNow;

// Ensure the SQLite schema is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await JkBmsLegacyOutboxMigrator.MigrateAsync(db);
    await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
        db,
        BmsOutboxPayloadTypes.Reading,
        BmsOutboxPayloadTypes.ReadingVersion);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.MapGet("/diagnostics/health", async (HttpContext httpContext, BmsPollerWorker poller, ForwarderCoordinator forwarder, OutboxDbContext db, IOptions<JkBmsOptions> bmsOptions, IOptions<OutboxOptions> outboxOptions, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var status = await CreateJkBmsDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, poller, forwarder, db, bmsOptions.Value, cancellationToken);
    return Results.Ok(status.Health);
});

app.MapGet("/diagnostics/status", async (HttpContext httpContext, BmsPollerWorker poller, ForwarderCoordinator forwarder, OutboxDbContext db, IOptions<JkBmsOptions> bmsOptions, IOptions<OutboxOptions> outboxOptions, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    return Results.Ok(await CreateJkBmsDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, poller, forwarder, db, bmsOptions.Value, cancellationToken));
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

app.MapGet("/diagnostics/devices", (HttpContext httpContext, BmsPollerWorker poller, IOptions<OutboxOptions> outboxOptions) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    return Results.Ok(poller.DeviceStates.Select(device => new
    {
        device.Alias,
        device.AdapterName,
        device.PollIntervalSeconds,
        IsOnline = device.LatestReading is not null && string.IsNullOrWhiteSpace(device.LastError),
        IsDegraded = device.LatestReading is not null && !string.IsNullOrWhiteSpace(device.LastError),
        LastPollAtUtc = device.LastPollAt,
        device.ConsecutiveErrors,
        device.LastError,
    }));
});

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});

await app.RunAsync();

static async Task<GatewayDiagnosticStatusResponse> CreateJkBmsDiagnosticStatusAsync(
    DateTime startedAtUtc,
    string environmentName,
    BmsPollerWorker poller,
    ForwarderCoordinator forwarder,
    OutboxDbContext db,
    JkBmsOptions options,
    CancellationToken cancellationToken)
{
    var now = DateTime.UtcNow;
    var deviceCounts = new GatewayDeviceCounts(
        poller.DeviceStates.Count,
        poller.DeviceStates.Count(device => device.LatestReading is not null && string.IsNullOrWhiteSpace(device.LastError)),
        poller.DeviceStates.Count(device => device.LatestReading is not null && !string.IsNullOrWhiteSpace(device.LastError)),
        poller.DeviceStates.Count(device => device.LatestReading is null));
    var state = deviceCounts.Offline > 0 ? GatewayHealthState.Critical : deviceCounts.Degraded > 0 ? GatewayHealthState.Warning : GatewayHealthState.Healthy;

    return new GatewayDiagnosticStatusResponse(
        "1.0",
        new GatewayIdentity("jkbms", "JK BMS Gateway", GatewayDomain.Power, "jkbms", RuntimeHost: Environment.MachineName),
        new GatewayRuntimeInfo(startedAtUtc, now, now - startedAtUtc, environmentName),
        new GatewayHealthSnapshot(
            state,
            now,
            BuildDeviceAlerts(poller.DeviceStates),
            state == GatewayHealthState.Healthy ? GatewaySampleState.Live : GatewaySampleState.Error,
            forwarder.FailedCount > 0 ? "failed-records-present" : forwarder.PendingCount > 0 ? "pending-forward" : "current",
            string.IsNullOrWhiteSpace(forwarder.LastError) ? "healthy" : "error"),
        deviceCounts,
        await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken),
        GatewayTelemetry.CreateDiagnostics("hvo-jkbms", [.. BmsTelemetry.CompatibilityMetricNames]),
        new Dictionary<string, string>
        {
            ["health"] = "/diagnostics/health",
            ["status"] = "/diagnostics/status",
            ["outbox"] = "/diagnostics/outbox",
            ["devices"] = "/diagnostics/devices",
        });
}

static IReadOnlyList<GatewayHealthAlert> BuildDeviceAlerts(IEnumerable<DevicePollState> devices) =>
    devices
        .Where(device => !string.IsNullOrWhiteSpace(device.LastError) || device.LatestReading is null)
        .Select(device => new GatewayHealthAlert(
            device.LatestReading is null ? "bms-waiting" : "bms-error",
            device.LatestReading is null ? GatewayAlertSeverity.Warning : GatewayAlertSeverity.Critical,
            $"BMS '{device.Alias}' is not healthy."))
        .ToArray();

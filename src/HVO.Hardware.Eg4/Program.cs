using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Logging;
using HVO.Edge.Hosting.Telemetry;
using HVO.Edge.Outbox;
using HVO.Enterprise.Telemetry;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Enterprise.Telemetry.Http;
using HVO.Enterprise.Telemetry.OpenTelemetry;
using HVO.Hardware.Eg4.Components;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using HVO.Hardware.Eg4.Hosting;
using HVO.Hardware.Eg4.Outbox;
using HVO.Hardware.Eg4.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseHvoGatewayLogging(new GatewayLogIdentity("hvo-eg4", "eg4", "battery-gateway"));
builder.Services.AddOptions<OutboxOptions>()
    .BindConfiguration(OutboxOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options =>
        Uri.TryCreate(options.ApiEndpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)),
        "Outbox:ApiEndpoint must be an absolute HTTPS URI (loopback HTTP is allowed for local testing).")
    .ValidateOnStart();

builder.Services.AddTelemetry(builder.Configuration.GetSection("Telemetry"));
builder.Services.AddOpenTelemetryExport(options =>
{
    options.EnableTraceExport = false;
    options.EnableMetricsExport = false;
    options.EnableLogExport = false;
    options.EnableStandardMeters = true;
    options.AdditionalMeterNames.Add(GatewayTelemetryConventions.MeterName);
    options.AdditionalActivitySources.Add(GatewayTelemetryConventions.ActivitySourceName);
});
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddAttributes(GatewayTelemetryResource.Create(
            "hvo-eg4", new GatewayTelemetryIdentity("eg4", "battery-gateway", "hvo"), builder.Environment.EnvironmentName)))
        .WithTracing(tracing => tracing.AddOtlpExporter())
        .WithMetrics(metrics => metrics.AddOtlpExporter());
}
builder.Services.AddTelemetryStatistics();
builder.Services.AddTelemetryHealthCheck();
builder.Services.AddSingleton(new GatewayTelemetry(new GatewayTelemetryIdentity("eg4", "battery-gateway")));

var outboxConfig = builder.Configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();
var dbPath = !string.IsNullOrWhiteSpace(outboxConfig?.DbPath)
    ? outboxConfig.DbPath
    : Path.Combine(builder.Environment.ContentRootPath, "outbox.db");
builder.Services.AddDbContext<OutboxDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Default Timeout=30", sqlite => sqlite.CommandTimeout(30)));
builder.Services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
builder.Services.AddScoped<IEg4PowerOutboxWriter, PowerOutboxWriter>();
builder.Services.AddSingleton<RuntimeOutboxSettings>();
builder.Services.AddHttpClient("PowerApi", (services, client) =>
{
    var options = services.GetRequiredService<IOptions<OutboxOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.ApiKey)) client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(120);
}).AddHttpMessageHandler(services => new TelemetryHttpMessageHandler(
    new HttpInstrumentationOptions { CaptureRequestHeaders = false, CaptureResponseHeaders = false },
    services.GetService<ILogger<TelemetryHttpMessageHandler>>()))
.AddStandardResilienceHandler(options =>
{
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
});

builder.Services.AddSingleton<PowerApiForwarder>();
builder.Services.AddHostedService(services => services.GetRequiredService<PowerApiForwarder>());
builder.Services.AddEg4GatewayCore(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IEg4OutboxDashboardProvider, Eg4RuntimeOutboxDashboardProvider>();
builder.Services.AddSingleton<Eg4FleetWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<Eg4FleetWorker>());
builder.Services.AddHealthChecks()
    .AddCheck<TelemetryHealthCheck>("telemetry")
    .AddCheck<Eg4DashboardHealthCheck>("eg4-gateway");
builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
var gatewayStartedAtUtc = DateTime.UtcNow;

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
        db, Eg4OutboxPayloadTypes.Reading, Eg4OutboxPayloadTypes.ReadingVersion);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("EG4 gateway encountered an unexpected error.");
    }));
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapGet("/diagnostics/health", (HttpContext context, IEg4GatewayDashboardState dashboard, IOptions<OutboxOptions> options) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(context, options.Value.ApiKey)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    return Results.Ok(CreateHealthSnapshot(dashboard.GetSnapshot(), options.Value, DateTime.UtcNow));
});
app.MapGet("/diagnostics/devices", (HttpContext context, IEg4GatewayDashboardState dashboard, IOptions<OutboxOptions> options) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(context, options.Value.ApiKey)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    return Results.Ok(dashboard.GetSnapshot().Devices);
});
app.MapGet("/diagnostics/outbox", async (HttpContext context, OutboxDbContext db, IOptions<OutboxOptions> options, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(context, options.Value.ApiKey)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    return Results.Ok(await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken));
});
app.MapPut("/diagnostics/outbox/settings", (HttpContext context, IEg4GatewayDashboardState dashboard, IOptions<OutboxOptions> options, Eg4OutboxSettingsUpdate? update) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(context, options.Value.ApiKey)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (update is null) return Results.BadRequest("Request body is required.");
    try { return Results.Ok(dashboard.UpdateOutboxSettings(update)); }
    catch (ArgumentOutOfRangeException exception) { return Results.BadRequest(exception.Message); }
});
app.MapGet("/diagnostics/status", async (HttpContext context, IEg4GatewayDashboardState dashboard, OutboxDbContext db, IOptions<OutboxOptions> options, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(context, options.Value.ApiKey)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var now = DateTime.UtcNow;
    var snapshot = dashboard.GetSnapshot();
    return Results.Ok(new GatewayDiagnosticStatusResponse(
        "1.0",
        new GatewayIdentity("eg4", "EG4 Battery Gateway", GatewayDomain.Power, "eg4-fleet", RuntimeHost: Environment.MachineName),
        new GatewayRuntimeInfo(gatewayStartedAtUtc, now, now - gatewayStartedAtUtc, app.Environment.EnvironmentName),
        CreateHealthSnapshot(snapshot, options.Value, now),
        new GatewayDeviceCounts(snapshot.ActiveCount, snapshot.OnlineCount, snapshot.DegradedCount, snapshot.OfflineCount),
        await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken),
        GatewayTelemetry.CreateDiagnostics("hvo-eg4"),
        new Dictionary<string, string>
        {
            ["health"] = "/diagnostics/health",
            ["status"] = "/diagnostics/status",
            ["devices"] = "/diagnostics/devices",
            ["outbox"] = "/diagnostics/outbox",
        }));
});

await app.RunAsync();

static GatewayHealthSnapshot CreateHealthSnapshot(Eg4GatewayDashboardSnapshot snapshot, OutboxOptions options, DateTime now)
{
    var outboxHealth = EdgeOutboxHealthEvaluator.Evaluate(
        new EdgeOutboxObservation(
            snapshot.Outbox.PendingCount,
            snapshot.Outbox.FailedCount,
            snapshot.Outbox.LastSentAtUtc,
            snapshot.Outbox.LastBatchCount,
            snapshot.Outbox.LastError,
            snapshot.Outbox.PermanentFailedCount,
            snapshot.Outbox.RetryExhaustedCount),
        new EdgeOutboxHealthOptions(options.PendingWarningCount, options.FailedCriticalCount));
    var alerts = new List<GatewayHealthAlert>(outboxHealth.Alerts);
    var state = snapshot.HealthState switch
    {
        Eg4DashboardHealthState.Healthy => GatewayHealthState.Healthy,
        Eg4DashboardHealthState.Degraded or Eg4DashboardHealthState.Misconfigured => GatewayHealthState.Warning,
        Eg4DashboardHealthState.Offline => GatewayHealthState.Critical,
        _ => GatewayHealthState.Unknown,
    };
    if (outboxHealth.HealthState == GatewayHealthState.Critical) state = GatewayHealthState.Critical;
    else if (outboxHealth.HealthState == GatewayHealthState.Warning && state == GatewayHealthState.Healthy) state = GatewayHealthState.Warning;
    if (snapshot.Outbox.IsRuntimeAvailable && snapshot.Outbox.ForwardingStatus == "Not configured")
    {
        alerts.Add(new GatewayHealthAlert(
            "outbox-not-configured",
            GatewayAlertSeverity.Warning,
            "EG4 outbox forwarding is not configured."));
        if (state == GatewayHealthState.Healthy) state = GatewayHealthState.Warning;
    }
    var freshness = snapshot.OnlineCount > 0
        ? GatewaySampleState.Live
        : snapshot.DegradedCount > 0
            ? GatewaySampleState.Stale
            : snapshot.ActiveCount == 0
                ? GatewaySampleState.Disabled
                : GatewaySampleState.Error;
    return new GatewayHealthSnapshot(
        state,
        now,
        alerts,
        freshness,
        outboxHealth.HistoricalFailureState.ToString().ToLowerInvariant(),
        outboxHealth.CurrentSyncState.ToString().ToLowerInvariant());
}

public partial class Program;

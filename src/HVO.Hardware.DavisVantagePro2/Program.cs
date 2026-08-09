using HVO.Enterprise.Telemetry;
using HVO.Enterprise.Telemetry.Http;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Enterprise.Telemetry.OpenTelemetry;
using HVO.Edge.Hosting.Logging;
using HVO.Edge.Hosting.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using HVO.Hardware.DavisVantagePro2.Components;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Services;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Api;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Workers;
using HVO.Edge.Outbox;
using HVO.Edge.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseHvoGatewayLogging(new GatewayLogIdentity(
    "hvo-davis",
    "davis",
    "weather-station",
    builder.Configuration["Station:StationId"]));

// ── Telemetry ───────────────────────────────────────────────────────────────────────────────────────
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
    options.AdditionalMeterNames.Add("hvo.davis");
    options.AdditionalMeterNames.Add(GatewayTelemetryConventions.MeterName);
    options.AdditionalActivitySources.Add(GatewayTelemetryConventions.ActivitySourceName);
});
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddAttributes(GatewayTelemetryResource.Create(
            "hvo-davis", new GatewayTelemetryIdentity("davis", "weather-station", "hvo"), builder.Environment.EnvironmentName)))
        .WithTracing(tb => tb.AddOtlpExporter())
        .WithMetrics(mb => mb.AddOtlpExporter());
}
builder.Services.AddSingleton<DavisTelemetry>();
builder.Services.AddTelemetryStatistics();
builder.Services.AddTelemetryHealthCheck();
builder.Services.AddHealthChecks()
    .AddCheck<TelemetryHealthCheck>("telemetry")
    .AddCheck<VantageStationHealthCheck>("station");

// ── Options ────────────────────────────────────────────────────────────────────
builder.Services
    .AddOptions<StationOptions>()
    .BindConfiguration(StationOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<OutboxOptions>()
    .BindConfiguration(OutboxOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Davis station services ─────────────────────────────────────────────────────
builder.Services.AddSingleton(sp =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StationOptions>>().Value;
    return new DavisConsoleClient(opt.Host, opt.Port,
        TimeSpan.FromSeconds(opt.SocketTimeoutSeconds),
        sp.GetRequiredService<ILogger<DavisConsoleClient>>());
});
builder.Services.AddSingleton<VantageStation>();

// ── SQLite local persistence and outbox ────────────────────────────────────────
var outboxConfig = builder.Configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();
string dbPath = OutboxDatabasePath.Resolve(outboxConfig?.DbPath);
string? dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (dir is null)
{
    throw new InvalidOperationException(
        $"The resolved outbox database path '{dbPath}' is a root directory; a file path is required.");
}
Directory.CreateDirectory(dir);
var localDbPath = Path.Combine(dir, "davis-local.db");
string? localDir = Path.GetDirectoryName(Path.GetFullPath(localDbPath));
if (localDir is null)
{
    throw new InvalidOperationException(
        $"The resolved Davis local database path '{localDbPath}' is a root directory; a file path is required.");
}
Directory.CreateDirectory(localDir);
builder.Services.AddDbContext<DavisLocalDbContext>(o =>
    o.UseSqlite($"Data Source={localDbPath}"),
    ServiceLifetime.Scoped);
builder.Services.AddDbContext<OutboxDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath};Default Timeout=30", sqlite => sqlite.CommandTimeout(30)),
    ServiceLifetime.Scoped);
builder.Services.AddSingleton<RuntimeOutboxSettings>();
builder.Services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
builder.Services.AddScoped<DavisOutboxWriter>();
builder.Services.AddSingleton<StationSettingsSnapshotStore>();
builder.Services.AddSingleton<StationInfoSnapshotStore>();

// ── HTTP client for outbox forwarder ────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("WeatherApi", (sp, client) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    client.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(120);
})
.AddHttpMessageHandler(sp => new TelemetryHttpMessageHandler(
    new HttpInstrumentationOptions { CaptureRequestHeaders = false, CaptureResponseHeaders = false },
    sp.GetService<ILogger<TelemetryHttpMessageHandler>>()))
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
});

// ── Background workers ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<WeatherStationWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WeatherStationWorker>());
builder.Services.AddSingleton<OutboxForwarder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxForwarder>());
builder.Services.AddSingleton<DavisSiteState>();

// ── Blazor Server ──────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();
var gatewayStartedAtUtc = DateTime.UtcNow;

// Ensure the SQLite schemas are created on startup
using (var scope = app.Services.CreateScope())
{
    var localDb = scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>();
    await localDb.Database.EnsureCreatedAsync();

    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    var stationOptions = scope.ServiceProvider.GetRequiredService<IOptions<StationOptions>>().Value;
    await DavisLegacyOutboxMigrator.MigrateAsync(db, stationOptions.StationId);
    await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
        db,
        DavisOutboxPayloadTypes.Raw,
        DavisOutboxPayloadTypes.RawVersion);

    var snapshotStore = scope.ServiceProvider.GetRequiredService<StationSettingsSnapshotStore>();
    var station = scope.ServiceProvider.GetRequiredService<VantageStation>();
    var cachedSnapshot = await snapshotStore.GetAsync();
    if (cachedSnapshot is not null)
    {
        station.ApplyStationSettings(cachedSnapshot.Settings);
    }
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

app.MapGet("/api/weather/current", (HttpContext httpContext, WeatherStationWorker worker, VantageStation station, OutboxForwarder forwarder, IOptions<OutboxOptions> outboxOptions) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var reading = worker.LatestReading;
    if (reading is null)
    {
        return Results.NoContent();
    }

    var observedAtUtc = worker.LastReadingAt ?? reading.RecordedAtUtc;
    var observedAtLocal = new DateTimeOffset(observedAtUtc, TimeSpan.Zero).ToOffset(station.ConsoleUtcOffset);

    var response = new CurrentConditionsResponse
    {
        ObservedAtUtc = observedAtUtc,
        ObservedAtLocal = observedAtLocal,
        ConsoleTimeZone = station.ConsoleTimeZoneLabel,
        IsConnected = station.IsConnected,
        TemperatureUnits = station.TemperatureUnits,
        BarometerUnits = station.BarometerUnits,
        RainUnits = station.RainUnits,
        WindUnits = station.WindUnits,
        OutsideTemperatureF = reading.OutsideTemperatureF,
        OutsideHumidityPercent = reading.OutsideHumidityPercent,
        DewPointF = reading.DewPointF,
        HeatIndexF = reading.HeatIndexF,
        WindChillF = reading.WindChillF,
        InsideTemperatureF = reading.InsideTemperatureF,
        InsideHumidityPercent = reading.InsideHumidityPercent,
        BarometricPressureInHg = reading.BarometricPressureInHg,
        PressureRawInHg = reading.PressureRawInHg,
        AltimeterInHg = reading.AltimeterInHg,
        BarometricTrend = reading.BarometricTrend,
        WindSpeedMph = reading.WindSpeedMph,
        WindDirectionDegrees = reading.WindDirectionDegrees,
        WindSpeed10MinAvgMph = reading.WindSpeed10MinAvgMph,
        WindGust10MinMph = reading.WindGust10MinMph,
        WindGust10MinDirectionDegrees = reading.WindGust10MinDirectionDegrees,
        RainRateInchesPerHour = reading.RainRateInchesPerHour,
        DailyRainInches = reading.DailyRainInches,
        Rain24HourInches = reading.Rain24HourInches,
        StormRainInches = reading.StormRainInches,
        DailyEtInches = reading.DailyEtInches,
        MonthlyRainInches = reading.MonthlyRainInches,
        YearlyRainInches = reading.YearlyRainInches,
        SolarRadiationWm2 = reading.SolarRadiationWm2,
        UvIndex = reading.UvIndex,
        Forecast = reading.ForecastString,
        Sunrise = reading.SunriseDisplay,
        Sunset = reading.SunsetDisplay,
        ConsoleBatteryVoltage = reading.ConsoleBatteryVoltage,
        TransmitterLowBatteryChannels = [.. reading.TransmitterLowBatteryChannels],
        PendingOutboxCount = forwarder.PendingCount,
        FailedOutboxCount = forwarder.FailedCount,
        Display = new DisplayCurrentConditionsResponse
        {
            TemperatureUnits = DisplayUnitConverter.TemperatureSuffix(station.TemperatureUnits),
            BarometerUnits = DisplayUnitConverter.PressureSuffix(station.BarometerUnits),
            RainUnits = DisplayUnitConverter.RainSuffix(station.RainUnits),
            WindUnits = DisplayUnitConverter.WindSuffix(station.WindUnits),
            OutsideTemperature = DisplayUnitConverter.Temperature(reading.OutsideTemperatureF, station.TemperatureUnits),
            DewPoint = DisplayUnitConverter.Temperature(reading.DewPointF, station.TemperatureUnits),
            HeatIndex = DisplayUnitConverter.Temperature(reading.HeatIndexF, station.TemperatureUnits),
            WindChill = DisplayUnitConverter.Temperature(reading.WindChillF, station.TemperatureUnits),
            InsideTemperature = DisplayUnitConverter.Temperature(reading.InsideTemperatureF, station.TemperatureUnits),
            BarometricPressure = DisplayUnitConverter.Pressure(reading.BarometricPressureInHg, station.BarometerUnits),
            PressureRaw = DisplayUnitConverter.Pressure(reading.PressureRawInHg, station.BarometerUnits),
            Altimeter = DisplayUnitConverter.Pressure(reading.AltimeterInHg, station.BarometerUnits),
            WindSpeed = DisplayUnitConverter.WindSpeed(reading.WindSpeedMph, station.WindUnits),
            WindSpeed10MinAvg = DisplayUnitConverter.WindSpeed(reading.WindSpeed10MinAvgMph, station.WindUnits),
            WindGust10Min = DisplayUnitConverter.WindSpeed(reading.WindGust10MinMph, station.WindUnits),
            RainRate = DisplayUnitConverter.Rain(reading.RainRateInchesPerHour, station.RainUnits),
            DailyRain = DisplayUnitConverter.Rain(reading.DailyRainInches, station.RainUnits),
            Rain24Hour = DisplayUnitConverter.Rain(reading.Rain24HourInches, station.RainUnits),
            StormRain = DisplayUnitConverter.Rain(reading.StormRainInches, station.RainUnits),
            DailyEt = DisplayUnitConverter.Rain(reading.DailyEtInches, station.RainUnits),
            MonthlyRain = DisplayUnitConverter.Rain(reading.MonthlyRainInches, station.RainUnits),
            YearlyRain = DisplayUnitConverter.Rain(reading.YearlyRainInches, station.RainUnits)
        }
    };

    return Results.Ok(response);
})
.WithName("GetCurrentWeather")
.WithTags("Weather");

app.MapGet("/diagnostics/health", async (HttpContext httpContext, WeatherStationWorker worker, VantageStation station, OutboxForwarder forwarder, OutboxDbContext db, IOptions<StationOptions> stationOptions, IOptions<OutboxOptions> outboxOptions, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var status = await CreateDavisDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, worker, station, forwarder, db, stationOptions.Value, cancellationToken);
    return Results.Ok(status.Health);
});

app.MapGet("/diagnostics/status", async (HttpContext httpContext, WeatherStationWorker worker, VantageStation station, OutboxForwarder forwarder, OutboxDbContext db, IOptions<StationOptions> stationOptions, IOptions<OutboxOptions> outboxOptions, CancellationToken cancellationToken) =>
{
    if (!GatewayDiagnosticsAuth.HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    return Results.Ok(await CreateDavisDiagnosticStatusAsync(gatewayStartedAtUtc, app.Environment.EnvironmentName, worker, station, forwarder, db, stationOptions.Value, cancellationToken));
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

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});

await app.RunAsync();

static async Task<GatewayDiagnosticStatusResponse> CreateDavisDiagnosticStatusAsync(
    DateTime startedAtUtc,
    string environmentName,
    WeatherStationWorker worker,
    VantageStation station,
    OutboxForwarder forwarder,
    OutboxDbContext db,
    StationOptions options,
    CancellationToken cancellationToken)
{
    var now = DateTime.UtcNow;
    var isFresh = worker.LastReadingAt is { } lastReadingAt && now - lastReadingAt <= TimeSpan.FromMinutes(5);
    var hasError = !string.IsNullOrWhiteSpace(worker.LastError);
    var health = new GatewayHealthSnapshot(
        hasError ? GatewayHealthState.Critical : isFresh ? GatewayHealthState.Healthy : GatewayHealthState.Warning,
        now,
        hasError ? [new GatewayHealthAlert("station-error", GatewayAlertSeverity.Critical, worker.LastError!)] : [],
        hasError ? GatewaySampleState.Error : isFresh ? GatewaySampleState.Live : GatewaySampleState.Stale,
        forwarder.FailedCount > 0 ? "failed-records-present" : forwarder.PendingCount > 0 ? "pending-forward" : "current",
        string.IsNullOrWhiteSpace(forwarder.LastError) ? "healthy" : "error");

    return new GatewayDiagnosticStatusResponse(
        "1.0",
        new GatewayIdentity("davis", "Davis Vantage Pro2 Gateway", GatewayDomain.Weather, options.StationId, RuntimeHost: Environment.MachineName),
        new GatewayRuntimeInfo(startedAtUtc, now, now - startedAtUtc, environmentName),
        health,
        GatewayDeviceCounts.SingleSource(isFresh, hasError),
        await EdgeOutboxDiagnosticsReader.ReadAsync(db, cancellationToken),
        GatewayTelemetry.CreateDiagnostics("hvo-davis", [.. DavisTelemetry.CompatibilityMetricNames]),
        new Dictionary<string, string>
        {
            ["health"] = "/diagnostics/health",
            ["status"] = "/diagnostics/status",
            ["outbox"] = "/diagnostics/outbox",
            ["legacyCurrent"] = "/api/weather/current",
        });
}

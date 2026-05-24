using HVO.Enterprise.Telemetry;
using HVO.Enterprise.Telemetry.Http;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Enterprise.Telemetry.OpenTelemetry;
using HVO.Enterprise.Telemetry.Serilog;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using HVO.Hardware.DavisVantagePro2.Components;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Services;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Api;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, _, loggerConfig) =>
{
    var logDir = Path.Combine(ctx.HostingEnvironment.ContentRootPath, "logs");
    Directory.CreateDirectory(logDir);

    loggerConfig
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("HVO.Hardware.DavisVantagePro2", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .Enrich.WithTelemetry()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            new CompactJsonFormatter(),
            Path.Combine(logDir, "davis-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 100_000_000,
            rollOnFileSizeLimit: true);

    // Forward logs to the OTel collector sidecar when the endpoint is configured.
    // OTEL_EXPORTER_OTLP_ENDPOINT is set in docker-compose; not set in development.
    var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "hvo-davis";
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

    // In Development, raise the Davis namespace to Debug so station communication is visible.
    if (ctx.HostingEnvironment.IsDevelopment())
    {
        loggerConfig
            .MinimumLevel.Override("HVO.Hardware.DavisVantagePro2", LogEventLevel.Debug)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information);
    }
});

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
    options.AdditionalActivitySources.Add("hvo.davis");
});
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tb => tb.AddOtlpExporter())
        .WithMetrics(mb => mb.AddOtlpExporter());
}
builder.Services.AddSingleton<DavisTelemetry>();
builder.Services.AddTelemetryStatistics();
builder.Services.AddTelemetryHealthCheck();
builder.Services.AddHealthChecks()
    .AddCheck<TelemetryHealthCheck>("telemetry");

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

// ── SQLite outbox ──────────────────────────────────────────────────────────────
var outboxConfig = builder.Configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();
string dbPath = !string.IsNullOrWhiteSpace(outboxConfig?.DbPath)
    ? outboxConfig.DbPath
    : Path.Combine(builder.Environment.ContentRootPath, "outbox.db");
builder.Services.AddDbContext<OutboxDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"),
    ServiceLifetime.Scoped);
builder.Services.AddSingleton<StationSettingsSnapshotStore>();
builder.Services.AddSingleton<StationInfoSnapshotStore>();

// ── HTTP client for outbox forwarder ────────────────────────────────────────────────────────────
builder.Services.AddHttpClient("WeatherApi", (sp, client) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    client.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler(sp => new TelemetryHttpMessageHandler(
    new HttpInstrumentationOptions { CaptureRequestHeaders = false, CaptureResponseHeaders = false },
    sp.GetService<ILogger<TelemetryHttpMessageHandler>>()));

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

// Ensure the SQLite schema is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE TABLE IF NOT EXISTS StationSettingsSnapshots (
            Id INTEGER NOT NULL CONSTRAINT PK_StationSettingsSnapshots PRIMARY KEY,
            SavedAtUtc TEXT NOT NULL,
            ArchiveIntervalSeconds INTEGER NOT NULL,
            LatitudeDegrees REAL NULL,
            LongitudeDegrees REAL NULL,
            AltitudeFeet REAL NULL,
            RainYearStartMonth INTEGER NOT NULL,
            RainBucketType INTEGER NOT NULL,
            DstSetting TEXT NOT NULL,
            UseTimezoneCode INTEGER NOT NULL,
            TimezoneCode INTEGER NOT NULL,
            GmtOffsetHours REAL NOT NULL,
            TemperatureLogging TEXT NOT NULL,
            BarometerUnits TEXT NOT NULL,
            TemperatureUnits TEXT NOT NULL,
            RainUnits TEXT NOT NULL,
            WindUnits TEXT NOT NULL
        );");
    await db.Database.ExecuteSqlRawAsync(
        @"CREATE TABLE IF NOT EXISTS StationInfoSnapshots (
            Id INTEGER NOT NULL CONSTRAINT PK_StationInfoSnapshots PRIMARY KEY,
            SavedAtUtc TEXT NOT NULL,
            HardwareName TEXT NOT NULL,
            HardwareType INTEGER NOT NULL,
            ModelType INTEGER NOT NULL,
            FirmwareVersion TEXT NOT NULL,
            FirmwareDate TEXT NOT NULL,
            ConsoleTime TEXT NOT NULL
        );");

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
    if (!HasMatchingApiKey(httpContext, outboxOptions.Value.ApiKey))
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
        FailedOutboxCount = forwarder.FailedCount
    };

    return Results.Ok(response);
})
.WithName("GetCurrentWeather")
.WithTags("Weather");

app.MapHealthChecks("/health");

await app.RunAsync();

static bool HasMatchingApiKey(HttpContext httpContext, string configuredApiKey)
{
    if (string.IsNullOrWhiteSpace(configuredApiKey) ||
        string.Equals(configuredApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
        configuredApiKey.Contains("__SET_", StringComparison.Ordinal))
    {
        return false;
    }

    return httpContext.Request.Headers.TryGetValue("X-Api-Key", out var providedApiKey)
        && providedApiKey.Count > 0
        && string.Equals(providedApiKey[0], configuredApiKey, StringComparison.Ordinal);
}

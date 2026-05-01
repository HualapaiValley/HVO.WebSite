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
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.EntityFrameworkCore;

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
            fileSizeLimitBytes: 100_000_000);

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
builder.Services.AddOpenTelemetry()
    .WithTracing(tb => tb.AddOtlpExporter())
    .WithMetrics(mb => mb.AddOtlpExporter());
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

// ── Blazor Server ──────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Ensure the SQLite schema is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await db.Database.EnsureCreatedAsync();
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

app.MapHealthChecks("/health");

await app.RunAsync();

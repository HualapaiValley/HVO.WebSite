using HVO.Hardware.DavisVantagePro2.Components;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

// ── HTTP client for outbox forwarder ──────────────────────────────────────────
builder.Services.AddHttpClient("WeatherApi", (sp, client) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    client.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
});

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

await app.RunAsync();

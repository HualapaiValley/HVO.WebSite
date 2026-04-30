using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Components;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Outbox.Forwarders;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Options ────────────────────────────────────────────────────────────────────
builder.Services
    .AddOptions<JkBmsOptions>()
    .BindConfiguration(JkBmsOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<OutboxOptions>()
    .BindConfiguration(OutboxOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── BLE transport ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IBmsTransportFactory, JkBmsBluetoothTransportFactory>();

// ── Alarm handler ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IBmsAlarmHandler, NullAlarmHandler>();

// ── SQLite outbox ──────────────────────────────────────────────────────────────
var outboxConfig = builder.Configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();
string dbPath = !string.IsNullOrWhiteSpace(outboxConfig?.DbPath)
    ? outboxConfig.DbPath
    : Path.Combine(builder.Environment.ContentRootPath, "outbox.db");
builder.Services.AddDbContext<OutboxDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"),
    ServiceLifetime.Scoped);

// ── HTTP client for outbox forwarder ──────────────────────────────────────────
builder.Services.AddHttpClient("OutboxForwarder", (sp, client) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
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

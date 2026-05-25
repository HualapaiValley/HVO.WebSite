using HVO.Hardware.VictronSmartShunt.Components;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

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
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            new CompactJsonFormatter(),
            Path.Combine(logDir, "smartshunt-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 100_000_000,
            rollOnFileSizeLimit: true);
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

var outboxConfig = builder.Configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>();
var dbPath = !string.IsNullOrWhiteSpace(outboxConfig?.DbPath)
    ? outboxConfig.DbPath
    : Path.Combine(builder.Environment.ContentRootPath, "outbox.db");

builder.Services.AddDbContext<OutboxDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<PowerOutboxWriter>();

builder.Services.AddHttpClient("PowerApi", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
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

builder.Services.AddHealthChecks()
    .AddCheck<SmartShuntGatewayHealthCheck>("smartshunt-gateway");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();
var exposeDiagnostics = app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Diagnostics:ExposeDetailedEndpoints", false);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
    await db.Database.EnsureCreatedAsync();
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

app.MapHealthChecks("/health");

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

app.MapGet("/gateway-health", (SmartShuntGatewayHealthService healthService) => Results.Ok(healthService.GetSnapshot()));

await app.RunAsync();

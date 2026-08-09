using HVO.Edge.Hosting.Logging;
using HVO.Hardware.Eg4.Components;
using HVO.Hardware.Eg4.Dashboard;
using HVO.Hardware.Eg4.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseHvoGatewayLogging(new GatewayLogIdentity("hvo-eg4", "eg4", "battery-gateway"));
builder.Services.AddEg4GatewayCore(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks().AddCheck<Eg4DashboardHealthCheck>("eg4-gateway");
builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
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
app.Run();

public partial class Program;

using HVO.Edge.Hosting.Logging;
using HVO.Hardware.Eg4.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseHvoGatewayLogging(new GatewayLogIdentity("hvo-eg4", "eg4", "battery-gateway"));
builder.Services.AddEg4GatewayCore(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapGet("/", () => Results.Ok(new { service = "hvo-eg4", status = "scaffold" }));
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
});
app.Run();

public partial class Program;

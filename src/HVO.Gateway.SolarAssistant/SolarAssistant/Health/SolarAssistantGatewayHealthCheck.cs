using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Gateway.SolarAssistant.SolarAssistant.Health;

public sealed class SolarAssistantGatewayHealthCheck(IGatewayHealthSnapshotProvider healthService) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = healthService.GetSnapshot();
        var status = snapshot.State switch
        {
            "critical" => HealthStatus.Unhealthy,
            "warning" => HealthStatus.Degraded,
            _ => HealthStatus.Healthy,
        };
        var description = snapshot.Alerts.Count == 0
            ? "SolarAssistant gateway is healthy."
            : string.Join("; ", snapshot.Alerts.Select(a => $"{a.Code}: {a.Message}"));

        return Task.FromResult(new HealthCheckResult(
            status,
            description,
            data: new Dictionary<string, object>
            {
                ["state"] = snapshot.State,
                ["alertCount"] = snapshot.Alerts.Count,
            }));
    }
}

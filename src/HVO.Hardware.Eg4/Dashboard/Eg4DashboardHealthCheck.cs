using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Hardware.Eg4.Dashboard;

public sealed class Eg4DashboardHealthCheck(IEg4GatewayDashboardState dashboardState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = dashboardState.GetSnapshot();
        var result = snapshot.HealthState switch
        {
            Eg4DashboardHealthState.Healthy => HealthCheckResult.Healthy($"{snapshot.OnlineCount} EG4 devices online."),
            Eg4DashboardHealthState.Degraded => HealthCheckResult.Degraded($"EG4 fleet has {snapshot.DegradedCount} degraded and {snapshot.OfflineCount} offline devices."),
            Eg4DashboardHealthState.Offline => HealthCheckResult.Unhealthy("All enabled EG4 devices are offline."),
            _ => HealthCheckResult.Degraded("No enabled EG4 devices are configured."),
        };
        return Task.FromResult(result);
    }
}

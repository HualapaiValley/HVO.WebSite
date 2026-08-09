using Microsoft.Extensions.Diagnostics.HealthChecks;
using HVO.Hardware.Eg4.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Dashboard;

public sealed class Eg4DashboardHealthCheck(
    IEg4GatewayDashboardState dashboardState,
    IOptions<OutboxOptions>? outboxOptions = null) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = dashboardState.GetSnapshot();
        var thresholds = outboxOptions?.Value ?? new OutboxOptions();
        var result = snapshot.HealthState switch
        {
            Eg4DashboardHealthState.Healthy => HealthCheckResult.Healthy($"{snapshot.OnlineCount} EG4 devices online."),
            Eg4DashboardHealthState.Degraded => HealthCheckResult.Degraded($"EG4 fleet has {snapshot.DegradedCount} degraded and {snapshot.OfflineCount} offline devices."),
            Eg4DashboardHealthState.Offline => HealthCheckResult.Unhealthy("All enabled EG4 devices are offline."),
            _ => HealthCheckResult.Degraded("No enabled EG4 devices are configured."),
        };
        if (snapshot.Outbox.FailedCount >= thresholds.FailedCriticalCount && thresholds.FailedCriticalCount > 0)
            result = HealthCheckResult.Unhealthy("EG4 outbox contains failed records.");
        else if (snapshot.Outbox.IsRuntimeAvailable && snapshot.Outbox.ForwardingStatus == "Not configured")
            result = HealthCheckResult.Degraded("EG4 outbox forwarding is not configured.");
        else if (result.Status == HealthStatus.Healthy && snapshot.Outbox.PendingCount > thresholds.PendingWarningCount)
            result = HealthCheckResult.Degraded("EG4 outbox backlog exceeds its warning threshold.");
        return Task.FromResult(result);
    }
}

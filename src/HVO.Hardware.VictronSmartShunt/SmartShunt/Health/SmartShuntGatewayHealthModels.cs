using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt.Health;

public enum SmartShuntGatewayHealthSeverity
{
    Info,
    Warning,
    Critical
}

public sealed class SmartShuntGatewayHealthAlert
{
    public string Code { get; init; } = string.Empty;
    public SmartShuntGatewayHealthSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class SmartShuntGatewayHealthSnapshot
{
    public DateTime EvaluatedAtUtc { get; init; }
    public string State { get; init; } = "unknown";
    public IReadOnlyList<SmartShuntGatewayHealthAlert> Alerts { get; init; } = [];
}

public interface ISmartShuntGatewayHealthSnapshotProvider
{
    SmartShuntGatewayHealthSnapshot GetSnapshot(DateTime? nowUtc = null);
}

public sealed class SmartShuntGatewayHealthCheck(ISmartShuntGatewayHealthSnapshotProvider healthService) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = healthService.GetSnapshot();
        IReadOnlyDictionary<string, object> data = new Dictionary<string, object>
        {
            ["state"] = snapshot.State
        };

        return Task.FromResult(snapshot.State switch
        {
            "healthy" => HealthCheckResult.Healthy(data: data),
            "warning" => HealthCheckResult.Degraded(string.Join(" | ", snapshot.Alerts.Select(a => $"{a.Code}: {a.Message}")), data: data),
            _ => HealthCheckResult.Unhealthy(string.Join(" | ", snapshot.Alerts.Select(a => $"{a.Code}: {a.Message}")), data: data)
        });
    }
}

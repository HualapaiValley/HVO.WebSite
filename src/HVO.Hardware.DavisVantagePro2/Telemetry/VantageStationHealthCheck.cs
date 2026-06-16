using HVO.Hardware.DavisVantagePro2.Station;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Hardware.DavisVantagePro2.Telemetry;

public sealed class VantageStationHealthCheck(VantageStation station) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(station.IsConnected
            ? HealthCheckResult.Healthy("Davis station is connected.")
            : HealthCheckResult.Unhealthy("Davis station is not connected."));
    }
}

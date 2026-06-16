using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Hardware.JkBms.Telemetry;

public sealed class BmsDeviceHealthCheck(BmsPollerWorker poller) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var states = poller.DeviceStates;
        if (states.Count == 0)
            return Task.FromResult(HealthCheckResult.Unhealthy("No BMS devices configured."));

        var connected = states.Count(s => s.IsSessionConnected);
        var total = states.Count;

        if (connected == 0)
            return Task.FromResult(HealthCheckResult.Unhealthy($"0 of {total} BMS device(s) connected."));

        if (connected < total)
            return Task.FromResult(HealthCheckResult.Degraded($"{connected} of {total} BMS device(s) connected."));

        return Task.FromResult(HealthCheckResult.Healthy($"All {total} BMS device(s) connected."));
    }
}

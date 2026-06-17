using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Gateway.TplinkKasa.Hosting;

public sealed class KasaGatewayHealthCheck(KasaGatewayState state) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await state.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.LastError is not null)
        {
            return HealthCheckResult.Degraded(status.LastError);
        }

        if (status.ConfiguredDeviceCount > 0 && status.OnlineDeviceCount == 0)
        {
            return HealthCheckResult.Unhealthy("No configured TP-Link/Kasa devices are online.");
        }

        if (status.OnlineDeviceCount < status.ConfiguredDeviceCount)
        {
            return HealthCheckResult.Degraded($"{status.OnlineDeviceCount} of {status.ConfiguredDeviceCount} configured TP-Link/Kasa device(s) are online.");
        }

        if (status.DegradedDeviceCount > 0)
        {
            return HealthCheckResult.Degraded("One or more TP-Link/Kasa devices are degraded.");
        }

        return HealthCheckResult.Healthy();
    }
}

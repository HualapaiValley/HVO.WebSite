using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Workers;

namespace HVO.Hardware.JkBms.Diagnostics;

internal sealed class JkBmsDiagnosticsSnapshotProvider(
    BmsPollerWorker poller,
    IHomeAssistantMqttProjection mqtt,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IEdgeDiagnosticsSnapshotProvider
{
    public async ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var online = 0;
        var degraded = 0;
        var offline = 0;
        var alerts = new List<GatewayHealthAlert>();
        foreach (var device in poller.DeviceStates)
        {
            var fresh = device.LastPollAt.HasValue
                && now - device.LastPollAt.Value <= TimeSpan.FromSeconds(device.PollIntervalSeconds * 2);
            if (fresh && device.IsSessionConnected && string.IsNullOrWhiteSpace(device.LastError))
                online++;
            else if (device.LatestReading is not null)
                degraded++;
            else if (device.SessionRequestFailureCount > 0 || device.ConsecutiveErrors > 0)
                offline++;
            else
                degraded++;

            if (!fresh || !device.IsSessionConnected || !string.IsNullOrWhiteSpace(device.LastError))
            {
                alerts.Add(new(
                    $"jkbms-{device.DeviceId}",
                    GatewayAlertSeverity.Warning,
                    device.LastPollAt.HasValue
                        ? $"{device.Alias} is unavailable ({FailureCategory(device)})."
                        : $"{device.Alias} is waiting for its first observation."));
            }
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<EdgeOutboxDiagnostics>().ReadAsync(cancellationToken);
        if (!outbox.Schema.IsCompatible)
            alerts.Add(new("outbox-schema", GatewayAlertSeverity.Critical, "The JK BMS outbox schema is incompatible."));
        if (outbox.FailedCount > 0)
            alerts.Add(new("outbox-failed", GatewayAlertSeverity.Warning, $"{outbox.FailedCount} outbox record(s) require attention."));
        if (outbox.PendingCount > 10)
            alerts.Add(new("outbox-pending", GatewayAlertSeverity.Warning, $"{outbox.PendingCount} outbox record(s) are pending."));
        if (!string.IsNullOrWhiteSpace(outbox.LastError))
            alerts.Add(new("outbox-forwarding", GatewayAlertSeverity.Warning, "Central forwarding is currently degraded."));

        var mqttStatus = mqtt.GetStatus();
        if (mqttStatus.Enabled && !mqttStatus.Connected)
            alerts.Add(new("home-assistant-mqtt", GatewayAlertSeverity.Warning, "Home Assistant MQTT presentation is disconnected."));

        GatewayHealthState healthState;
        if (poller.DeviceStates.Count == 0)
        {
            alerts.Add(new("jkbms-no-devices", GatewayAlertSeverity.Warning, "No JK BMS devices are enabled."));
            healthState = GatewayHealthState.Warning;
        }
        else if (!outbox.Schema.IsCompatible
            || online == 0 && poller.DeviceStates.Any(static device =>
                device.LastPollAt.HasValue || device.SessionRequestFailureCount > 0 || device.ConsecutiveErrors > 0))
            healthState = GatewayHealthState.Critical;
        else if (alerts.Count > 0 || degraded > 0 || offline > 0)
            healthState = GatewayHealthState.Warning;
        else
            healthState = GatewayHealthState.Healthy;

        var sampleState = online > 0
            ? GatewaySampleState.Live
            : poller.DeviceStates.Count == 0
                ? GatewaySampleState.Disabled
                : poller.DeviceStates.All(static device => !device.LastPollAt.HasValue && device.ConsecutiveErrors == 0)
                    ? GatewaySampleState.Waiting
                    : GatewaySampleState.Error;
        return new(
            new GatewayHealthSnapshot(
                healthState,
                now,
                alerts,
                sampleState,
                outbox.FailedCount > 0 ? "degraded" : "healthy",
                outbox.PendingCount > 0 ? "pending" : "synced"),
            new GatewayDeviceCounts(poller.DeviceStates.Count, online, degraded, offline));
    }

    private static string FailureCategory(DevicePollState device) =>
        device.IsSessionConnected ? "stale" : device.SessionRequestFailureCount > 0 ? "connect" : "disconnected";
}

using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Diagnostics;

internal sealed class Eg4DiagnosticsSnapshotProvider(
    Eg4RuntimeState state,
    IOptions<Eg4Options> options,
    IHomeAssistantMqttProjection mqtt,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IEdgeDiagnosticsSnapshotProvider
{
    public async ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var devices = state.Snapshot();
        var online = 0;
        var degraded = 0;
        var offline = 0;
        var alerts = new List<GatewayHealthAlert>();
        foreach (var runtime in devices)
        {
            var staleAfter = TimeSpan.FromSeconds(2 * (runtime.Device.PollIntervalSeconds ?? options.Value.DefaultPollIntervalSeconds));
            var fresh = runtime.LastObservation is not null && now - runtime.LastObservation.ObservedAtUtc <= staleAfter;
            if (fresh && runtime.FailureCategory is null)
                online++;
            else if (runtime.LastObservation is not null)
                degraded++;
            else if (runtime.LastAttemptAtUtc.HasValue)
                offline++;
            else
                degraded++;

            if (!fresh || runtime.FailureCategory is not null)
            {
                alerts.Add(new(
                    $"eg4-{runtime.Device.SourceId}",
                    runtime.Device.Type == Eg4DeviceType.ChargeControllerMppt10048Hv
                        ? GatewayAlertSeverity.Warning
                        : GatewayAlertSeverity.Critical,
                    runtime.LastAttemptAtUtc.HasValue
                        ? $"{runtime.Device.Alias} is unavailable ({runtime.FailureCategory ?? "stale"})."
                        : $"{runtime.Device.Alias} is waiting for its first observation."));
            }
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<EdgeOutboxDiagnostics>().ReadAsync(cancellationToken);
        if (!outbox.Schema.IsCompatible)
            alerts.Add(new("outbox-schema", GatewayAlertSeverity.Critical, "The EG4 outbox schema is incompatible."));
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
        if (devices.Count == 0)
        {
            alerts.Add(new("eg4-no-devices", GatewayAlertSeverity.Warning, "No EG4 devices are enabled."));
            healthState = GatewayHealthState.Warning;
        }
        else if (!outbox.Schema.IsCompatible
            || alerts.Any(static alert => alert.Severity == GatewayAlertSeverity.Critical) && online == 0)
            healthState = GatewayHealthState.Critical;
        else if (alerts.Count > 0 || degraded > 0 || offline > 0)
            healthState = GatewayHealthState.Warning;
        else
            healthState = GatewayHealthState.Healthy;

        var sampleState = online > 0
            ? GatewaySampleState.Live
            : devices.Count == 0
                ? GatewaySampleState.Disabled
                : devices.All(static device => !device.LastAttemptAtUtc.HasValue)
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
            new GatewayDeviceCounts(devices.Count, online, degraded, offline));
    }
}

using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Diagnostics;

internal sealed class SmartShuntDiagnosticsSnapshotProvider(
    SmartShuntWorker worker,
    IOptions<SmartShuntOptions> options,
    IHomeAssistantMqttProjection mqtt,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IEdgeDiagnosticsSnapshotProvider
{
    public async ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var fresh = worker.LastSnapshotAtUtc.HasValue && now - worker.LastSnapshotAtUtc.Value <= TimeSpan.FromSeconds(options.Value.SampleStaleAfterSeconds);
        var alerts = new List<GatewayHealthAlert>();
        if (!fresh || !worker.IsConnected)
            alerts.Add(new("smartshunt-public-gatt", worker.LastSnapshotAtUtc.HasValue ? GatewayAlertSeverity.Warning : GatewayAlertSeverity.Critical,
                worker.LastSnapshotAtUtc.HasValue ? "The direct public-GATT observation is stale or disconnected." : "Waiting for the first direct public-GATT observation."));
        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<EdgeOutboxDiagnostics>().ReadAsync(cancellationToken);
        if (!outbox.Schema.IsCompatible) alerts.Add(new("outbox-schema", GatewayAlertSeverity.Critical, "The SmartShunt outbox schema is incompatible."));
        if (outbox.FailedCount > 0 || !string.IsNullOrWhiteSpace(outbox.LastError)) alerts.Add(new("outbox-forwarding", GatewayAlertSeverity.Warning, "Central forwarding is degraded."));
        var mqttStatus = mqtt.GetStatus();
        if (mqttStatus.Enabled && !mqttStatus.Connected) alerts.Add(new("home-assistant-mqtt", GatewayAlertSeverity.Warning, "Home Assistant MQTT presentation is disconnected."));
        var state = alerts.Any(static alert => alert.Severity == GatewayAlertSeverity.Critical)
            ? GatewayHealthState.Critical
            : alerts.Count > 0 ? GatewayHealthState.Warning : GatewayHealthState.Healthy;
        return new(new GatewayHealthSnapshot(state, now, alerts,
            fresh ? GatewaySampleState.Live : worker.LastSnapshotAtUtc is null ? GatewaySampleState.Waiting : GatewaySampleState.Stale,
            outbox.FailedCount > 0 ? "degraded" : "healthy", outbox.PendingCount > 0 ? "pending" : "synced"),
            new GatewayDeviceCounts(1, fresh ? 1 : 0, worker.LastSnapshotAtUtc.HasValue && !fresh ? 1 : 0, worker.LastSnapshotAtUtc.HasValue ? 0 : 1));
    }
}

using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Workers;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.WeatherUnderground;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Diagnostics;

internal sealed class DavisDiagnosticsSnapshotProvider(
    DavisRuntimeState state,
    WeatherUndergroundPublisherState weatherUndergroundState,
    IOptions<WeatherUndergroundOptions> weatherUndergroundOptions,
    IHomeAssistantMqttProjection mqtt,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IEdgeDiagnosticsSnapshotProvider
{
    public async ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var fresh = state.LastReadingAtUtc.HasValue && now - state.LastReadingAtUtc.Value <= TimeSpan.FromMinutes(5);
        var alerts = new List<GatewayHealthAlert>();
        if (!state.IsConnected)
            alerts.Add(new("davis-station", GatewayAlertSeverity.Critical, state.LastError ?? "The Davis station is disconnected."));
        else if (!fresh)
            alerts.Add(new("davis-stale", GatewayAlertSeverity.Warning, "The latest Davis LOOP observation is stale."));
        if (!string.IsNullOrWhiteSpace(state.LastArchiveError))
            alerts.Add(new("davis-archive", GatewayAlertSeverity.Warning, "Davis archive recovery is currently degraded."));
        var weatherUnderground = weatherUndergroundState.Snapshot();
        if (weatherUndergroundOptions.Value.Enabled && weatherUnderground.ConsecutiveFailures > 0)
            alerts.Add(new("weather-underground", GatewayAlertSeverity.Warning, "Weather Underground delivery is currently degraded."));

        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<EdgeOutboxDiagnostics>().ReadAsync(cancellationToken);
        if (!outbox.Schema.IsCompatible)
            alerts.Add(new("outbox-schema", GatewayAlertSeverity.Critical, "The Davis outbox schema is incompatible."));
        if (outbox.FailedCount > 0)
            alerts.Add(new("outbox-failed", GatewayAlertSeverity.Warning, $"{outbox.FailedCount} outbox record(s) require attention."));
        if (outbox.PendingCount > 10)
            alerts.Add(new("outbox-pending", GatewayAlertSeverity.Warning, $"{outbox.PendingCount} outbox record(s) are pending."));
        var mqttStatus = mqtt.GetStatus();
        if (mqttStatus.Enabled && !mqttStatus.Connected)
            alerts.Add(new("home-assistant-mqtt", GatewayAlertSeverity.Warning, "Home Assistant MQTT presentation is disconnected."));

        var health = alerts.Any(static alert => alert.Severity == GatewayAlertSeverity.Critical)
            ? GatewayHealthState.Critical
            : alerts.Count > 0 ? GatewayHealthState.Warning : GatewayHealthState.Healthy;
        return new(
            new GatewayHealthSnapshot(
                health,
                now,
                alerts,
                fresh ? GatewaySampleState.Live : state.LastReadingAtUtc.HasValue ? GatewaySampleState.Stale : GatewaySampleState.Waiting,
                outbox.FailedCount > 0 ? "degraded" : "healthy",
                outbox.PendingCount > 0 ? "pending" : "synced"),
            GatewayDeviceCounts.SingleSource(fresh, !state.IsConnected),
            [new GatewayExternalDeliveryDiagnostics(
                "weather-underground",
                weatherUndergroundOptions.Value.Enabled,
                weatherUnderground.LastObservationAtUtc,
                weatherUnderground.LastAttemptAtUtc,
                weatherUnderground.LastSuccessAtUtc,
                weatherUnderground.ConsecutiveFailures,
                weatherUnderground.LastError)]);
    }
}

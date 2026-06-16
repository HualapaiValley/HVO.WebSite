using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Workers;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.SolarAssistant.Health;

public interface IGatewayHealthSnapshotProvider
{
    SolarAssistantGatewayHealthSnapshot GetSnapshot(DateTime? nowUtc = null);
}

public interface IGatewayStatusPayloadProvider
{
    GatewayStatusPayload CreatePayload(DateTime? nowUtc = null);
}

public sealed class SolarAssistantGatewayHealthService : IGatewayHealthSnapshotProvider, IGatewayStatusPayloadProvider
{
    private readonly SolarAssistantSnapshotWorker _snapshotWorker;
    private readonly SolarAssistantMqttDiscoveryWorker _mqttWorker;
    private readonly PowerApiForwarder _forwarder;
    private readonly SolarAssistantOptions _options;

    public SolarAssistantGatewayHealthService(
        SolarAssistantSnapshotWorker snapshotWorker,
        SolarAssistantMqttDiscoveryWorker mqttWorker,
        PowerApiForwarder forwarder,
        IOptions<SolarAssistantOptions> options)
    {
        _snapshotWorker = snapshotWorker;
        _mqttWorker = mqttWorker;
        _forwarder = forwarder;
        _options = options.Value;
    }

    public SolarAssistantGatewayHealthSnapshot GetSnapshot(DateTime? nowUtc = null) => Evaluate(
        _options,
        _snapshotWorker.LastSnapshotAt,
        _snapshotWorker.LastError,
        _snapshotWorker.LastSnapshot,
        _mqttWorker.Inventory,
        _forwarder.PendingCount,
        _forwarder.FailedCount,
        _forwarder.LastError,
        nowUtc ?? DateTime.UtcNow);

    public GatewayStatusPayload CreatePayload(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var mqttInventory = _mqttWorker.Inventory;
        var health = GetSnapshot(now);
        return CreatePayload(
            _options,
            _snapshotWorker.LastSnapshotAt,
            _snapshotWorker.LastError,
            _snapshotWorker.LastMetricCount,
            mqttInventory,
            _forwarder.PendingCount,
            _forwarder.FailedCount,
            _forwarder.LastSentAt,
            _forwarder.LastBatchCount,
            _forwarder.LastError,
            health,
            now);
    }

    public static GatewayStatusPayload CreatePayload(
        SolarAssistantOptions options,
        DateTime? lastSnapshotAtUtc,
        string? snapshotError,
        int restMetricCount,
        SolarAssistantMqttInventory mqttInventory,
        int pendingOutboxCount,
        int failedOutboxCount,
        DateTime? lastSentAtUtc,
        int lastBatchCount,
        string? outboxError,
        SolarAssistantGatewayHealthSnapshot health,
        DateTime nowUtc) => new()
        {
            SourceId = options.TotalSourceId,
            SourceSystem = "solarassistant",
            DeviceId = options.TotalDeviceId,
            RecordedAtUtc = nowUtc,
            Identity = new GatewayIdentity(
                GatewayId: "solarassistant",
                DisplayName: "SolarAssistant Gateway",
                Domain: GatewayDomain.Power,
                SourceId: options.TotalSourceId,
                DeviceId: options.TotalDeviceId),
            Health = new GatewayHealthSnapshot(
                State: MapHealthState(health.State),
                EvaluatedAtUtc: health.EvaluatedAtUtc,
                Alerts: health.Alerts.Select(MapHealthAlert).ToArray(),
                SourceFreshness: RestState(options, lastSnapshotAtUtc, snapshotError, nowUtc),
                OutboxState: OutboxState(pendingOutboxCount, failedOutboxCount, outboxError),
                ApiSyncState: string.IsNullOrWhiteSpace(outboxError) ? "healthy" : "error"),
            Rest = new GatewayRuntimeSignal(
                State: RestState(options, lastSnapshotAtUtc, snapshotError, nowUtc),
                LastObservedAtUtc: lastSnapshotAtUtc,
                LastError: snapshotError,
                Detail: restMetricCount > 0 ? $"{restMetricCount} REST metric(s)" : null),
            Mqtt = options.EnableMqttDiscovery
                ? new GatewayRuntimeSignal(
                    State: MqttState(options, mqttInventory, nowUtc),
                    LastObservedAtUtc: mqttInventory.LastMessageAtUtc,
                    LastError: mqttInventory.LastError,
                    Detail: $"{mqttInventory.EntityCount} entit(ies), {mqttInventory.StateTopicCount} state topic(s)")
                : null,
            Outbox = new GatewayOutboxStatus(
                PendingCount: pendingOutboxCount,
                FailedCount: failedOutboxCount,
                LastSentAtUtc: lastSentAtUtc,
                LastBatchCount: lastBatchCount,
                LastError: outboxError),
            RestMetricCount = restMetricCount,
            MqttEntityCount = mqttInventory.EntityCount,
            MqttStateTopicCount = mqttInventory.StateTopicCount,
            MqttCommandTopicCount = mqttInventory.CommandTopicCount,
        };

    public static SolarAssistantGatewayHealthSnapshot Evaluate(
        SolarAssistantOptions options,
        DateTime? lastSnapshotAtUtc,
        string? snapshotError,
        PowerReadingPayload? snapshot,
        SolarAssistantMqttInventory mqttInventory,
        int pendingOutboxCount,
        int failedOutboxCount,
        string? outboxError,
        DateTime nowUtc)
    {
        var alerts = new List<SolarAssistantGatewayHealthAlert>();

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            alerts.Add(Alert("rest-disabled", SolarAssistantGatewayHealthSeverity.Warning, "SolarAssistant host is not configured."));
        }
        else if (!string.IsNullOrWhiteSpace(snapshotError))
        {
            alerts.Add(Alert("rest-error", SolarAssistantGatewayHealthSeverity.Critical, $"REST polling error: {snapshotError}"));
        }
        else if (lastSnapshotAtUtc is null)
        {
            alerts.Add(Alert("rest-waiting", SolarAssistantGatewayHealthSeverity.Warning, "Waiting for first REST snapshot."));
        }
        else if (nowUtc - lastSnapshotAtUtc.Value > TimeSpan.FromSeconds(options.RestStaleAfterSeconds))
        {
            alerts.Add(Alert("rest-stale", SolarAssistantGatewayHealthSeverity.Critical, "REST snapshot is stale."));
        }

        if (options.EnableMqttDiscovery)
        {
            var mqttFresh = mqttInventory.LastMessageAtUtc is not null &&
                nowUtc - mqttInventory.LastMessageAtUtc.Value <= TimeSpan.FromSeconds(options.MqttStaleAfterSeconds);
            if (!mqttFresh && !string.Equals(mqttInventory.ConnectionState, "connected", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(Alert("mqtt-disconnected", SolarAssistantGatewayHealthSeverity.Warning, $"MQTT discovery is {mqttInventory.ConnectionState}."));
            }

            if (mqttInventory.LastMessageAtUtc is null)
            {
                alerts.Add(Alert("mqtt-waiting", SolarAssistantGatewayHealthSeverity.Warning, "MQTT is connected but no discovery/state messages have arrived."));
            }
            else if (nowUtc - mqttInventory.LastMessageAtUtc.Value > TimeSpan.FromSeconds(options.MqttStaleAfterSeconds))
            {
                alerts.Add(Alert("mqtt-stale", SolarAssistantGatewayHealthSeverity.Warning, "MQTT discovery/state messages are stale."));
            }
        }

        alerts.AddRange(BuildOutboxAlerts(options, pendingOutboxCount, failedOutboxCount, outboxError));

        if (snapshot?.BatteryStateOfChargePercent is { } soc)
        {
            if (soc <= options.CriticalBatteryPercent)
                alerts.Add(Alert("battery-critical", SolarAssistantGatewayHealthSeverity.Critical, $"Battery state of charge is critical at {soc:0}%."));
            else if (soc <= options.LowBatteryWarningPercent)
                alerts.Add(Alert("battery-low", SolarAssistantGatewayHealthSeverity.Warning, $"Battery state of charge is low at {soc:0}%."));
        }

        if (options.HighLoadWarningW > 0 && snapshot?.LoadPowerW is { } loadPower && loadPower >= options.HighLoadWarningW)
            alerts.Add(Alert("load-high", SolarAssistantGatewayHealthSeverity.Warning, $"Load power is high at {loadPower:0} W."));

        if (options.BatteryDischargeWarningW > 0 && snapshot?.BatteryPowerW is { } batteryPower && batteryPower >= options.BatteryDischargeWarningW)
            alerts.Add(Alert("battery-discharge-high", SolarAssistantGatewayHealthSeverity.Warning, $"Battery discharge power is high at {batteryPower:0} W."));

        return new SolarAssistantGatewayHealthSnapshot
        {
            EvaluatedAtUtc = nowUtc,
            State = alerts.Any(a => a.Severity == SolarAssistantGatewayHealthSeverity.Critical)
                ? "critical"
                : alerts.Any(a => a.Severity == SolarAssistantGatewayHealthSeverity.Warning) ? "warning" : "healthy",
            Alerts = alerts,
        };
    }

    private static SolarAssistantGatewayHealthAlert Alert(
        string code,
        SolarAssistantGatewayHealthSeverity severity,
        string message) => new()
        {
            Code = code,
            Severity = severity,
            Message = message,
        };

    private static IReadOnlyList<SolarAssistantGatewayHealthAlert> BuildOutboxAlerts(
        SolarAssistantOptions options,
        int pendingOutboxCount,
        int failedOutboxCount,
        string? outboxError)
    {
        var evaluation = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(
                PendingCount: pendingOutboxCount,
                FailedCount: failedOutboxCount,
                LastError: outboxError,
                PermanentFailedCount: failedOutboxCount),
            new EdgeOutboxHealthOptions(
                PendingWarningCount: options.OutboxPendingWarningCount,
                FailedCriticalCount: options.OutboxFailedCriticalCount));

        return evaluation.Alerts
            .Select(MapOutboxAlert)
            .ToArray();
    }

    private static SolarAssistantGatewayHealthAlert MapOutboxAlert(GatewayHealthAlert alert)
    {
        return Alert(
            MapOutboxAlertCode(alert.Code),
            alert.Severity switch
            {
                GatewayAlertSeverity.Critical => SolarAssistantGatewayHealthSeverity.Critical,
                GatewayAlertSeverity.Warning => SolarAssistantGatewayHealthSeverity.Warning,
                _ => SolarAssistantGatewayHealthSeverity.Info,
            },
            alert.Message);
    }

    private static string MapOutboxAlertCode(string code) => code switch
    {
        "outbox-current-sync-failing" => "outbox-error",
        "outbox-pending-backlog" => "outbox-backlog",
        "outbox-historical-failures" or "outbox-historical-failures-over-threshold"
            or "outbox-permanent-failures" or "outbox-retry-exhausted" => "outbox-failed",
        _ => code,
    };

    private static GatewayHealthState MapHealthState(string state) => state switch
    {
        "healthy" => GatewayHealthState.Healthy,
        "warning" => GatewayHealthState.Warning,
        "critical" => GatewayHealthState.Critical,
        _ => GatewayHealthState.Unknown,
    };

    private static GatewayHealthAlert MapHealthAlert(SolarAssistantGatewayHealthAlert alert) => new(
        Code: alert.Code,
        Severity: alert.Severity switch
        {
            SolarAssistantGatewayHealthSeverity.Critical => GatewayAlertSeverity.Critical,
            SolarAssistantGatewayHealthSeverity.Warning => GatewayAlertSeverity.Warning,
            _ => GatewayAlertSeverity.Info,
        },
        Message: alert.Message);

    private static GatewaySampleState RestState(
        SolarAssistantOptions options,
        DateTime? lastSnapshotAtUtc,
        string? snapshotError,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
            return GatewaySampleState.Disabled;

        if (!string.IsNullOrWhiteSpace(snapshotError))
            return GatewaySampleState.Error;

        if (lastSnapshotAtUtc is null)
            return GatewaySampleState.Waiting;

        return nowUtc - lastSnapshotAtUtc.Value > TimeSpan.FromSeconds(options.RestStaleAfterSeconds)
            ? GatewaySampleState.Stale
            : GatewaySampleState.Live;
    }

    private static GatewaySampleState MqttState(SolarAssistantOptions options, SolarAssistantMqttInventory inventory, DateTime nowUtc)
    {
        if (!options.EnableMqttDiscovery || string.Equals(inventory.ConnectionState, "disabled", StringComparison.OrdinalIgnoreCase))
            return GatewaySampleState.Disabled;

        if (!string.IsNullOrWhiteSpace(inventory.LastError))
            return GatewaySampleState.Error;

        if (inventory.LastMessageAtUtc is null)
            return GatewaySampleState.Waiting;

        return nowUtc - inventory.LastMessageAtUtc.Value > TimeSpan.FromSeconds(options.MqttStaleAfterSeconds)
            ? GatewaySampleState.Stale
            : GatewaySampleState.Live;
    }

    private static string OutboxState(int pendingCount, int failedCount, string? lastError)
    {
        if (!string.IsNullOrWhiteSpace(lastError))
            return "error";

        if (failedCount > 0)
            return "warning";

        return pendingCount > 0 ? "pending" : "healthy";
    }
}

using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.SolarAssistant.Health;

public interface IGatewayHealthSnapshotProvider
{
    SolarAssistantGatewayHealthSnapshot GetSnapshot(DateTime? nowUtc = null);
}

public sealed class SolarAssistantGatewayHealthService : IGatewayHealthSnapshotProvider
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
            if (!string.Equals(mqttInventory.ConnectionState, "connected", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(Alert("mqtt-disconnected", SolarAssistantGatewayHealthSeverity.Warning, $"MQTT discovery is {mqttInventory.ConnectionState}."));
            }
            else if (mqttInventory.LastMessageAtUtc is null)
            {
                alerts.Add(Alert("mqtt-waiting", SolarAssistantGatewayHealthSeverity.Warning, "MQTT is connected but no discovery/state messages have arrived."));
            }
            else if (nowUtc - mqttInventory.LastMessageAtUtc.Value > TimeSpan.FromSeconds(options.MqttStaleAfterSeconds))
            {
                alerts.Add(Alert("mqtt-stale", SolarAssistantGatewayHealthSeverity.Warning, "MQTT discovery/state messages are stale."));
            }
        }

        if (failedOutboxCount >= options.OutboxFailedCriticalCount && options.OutboxFailedCriticalCount > 0)
        {
            alerts.Add(Alert("outbox-failed", SolarAssistantGatewayHealthSeverity.Critical, $"{failedOutboxCount} outbox record(s) failed."));
        }

        if (pendingOutboxCount > options.OutboxPendingWarningCount)
        {
            alerts.Add(Alert("outbox-backlog", SolarAssistantGatewayHealthSeverity.Warning, $"{pendingOutboxCount} outbox record(s) are pending."));
        }

        if (!string.IsNullOrWhiteSpace(outboxError))
        {
            alerts.Add(Alert("outbox-error", SolarAssistantGatewayHealthSeverity.Warning, $"Outbox forwarder error: {outboxError}"));
        }

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
}

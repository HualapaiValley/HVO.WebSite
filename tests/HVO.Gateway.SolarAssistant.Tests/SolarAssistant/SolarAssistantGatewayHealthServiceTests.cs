using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantGatewayHealthServiceTests
{
    [TestMethod]
    public void Evaluate_ReturnsHealthy_WhenInputsAreFreshAndNormal()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("connected", now.AddSeconds(-15)),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("healthy");
        health.Alerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Evaluate_ReturnsCritical_WhenRestSnapshotIsStale()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-121),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("connected", now.AddSeconds(-15)),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("critical");
        health.Alerts.Should().ContainSingle(a =>
            a.Code == "rest-stale" &&
            a.Severity == SolarAssistantGatewayHealthSeverity.Critical);
    }

    [TestMethod]
    public void Evaluate_ReturnsWarnings_ForMqttStaleAndOutboxBacklog()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("connected", now.AddSeconds(-121)),
            pendingOutboxCount: 11,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("warning");
        health.Alerts.Select(a => a.Code).Should().Contain(["mqtt-stale", "outbox-backlog"]);
        health.Alerts.Should().OnlyContain(a => a.Severity == SolarAssistantGatewayHealthSeverity.Warning);
    }

    [TestMethod]
    public void Evaluate_DoesNotWarnForMqttReconnect_WhenMessagesAreFresh()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("disconnected", now.AddSeconds(-15)),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("healthy");
        health.Alerts.Should().NotContain(a => a.Code == "mqtt-disconnected");
    }

    [TestMethod]
    public void Evaluate_ReturnsWarning_ForMqttReconnect_WhenMessagesAreStale()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("connecting", now.AddSeconds(-121)),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("warning");
        health.Alerts.Select(a => a.Code).Should().Contain(["mqtt-disconnected", "mqtt-stale"]);
    }

    [TestMethod]
    public void Evaluate_ReturnsWarning_ForHistoricalFailedOutbox_WhenCurrentForwardingIsNotFailing()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("connected", now.AddSeconds(-15)),
            pendingOutboxCount: 0,
            failedOutboxCount: 71,
            outboxError: null,
            now);

        health.State.Should().Be("warning");
        health.Alerts.Should().ContainSingle(a =>
            a.Code == "outbox-failed" &&
            a.Severity == SolarAssistantGatewayHealthSeverity.Warning);
    }

    [TestMethod]
    public void Evaluate_ReturnsCritical_ForCurrentOutboxFailureAndCriticalBattery()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 15, loadPower: 900, batteryPower: 200),
            Mqtt("connected", now.AddSeconds(-15)),
            pendingOutboxCount: 1,
            failedOutboxCount: 0,
            outboxError: "HTTP 401",
            now);

        health.State.Should().Be("critical");
        health.Alerts.Select(a => a.Code).Should().Contain(["outbox-error", "battery-critical"]);
        health.Alerts.Should().Contain(a => a.Severity == SolarAssistantGatewayHealthSeverity.Critical);
    }

    [TestMethod]
    public void Evaluate_ReturnsCritical_ForCurrentOutboxFailureWithoutPendingRows()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("connected", now.AddSeconds(-15)),
            pendingOutboxCount: 0,
            failedOutboxCount: 1,
            outboxError: "website validation rejected payload",
            now);

        health.State.Should().Be("critical");
        health.Alerts.Should().Contain(a =>
            a.Code == "outbox-error" &&
            a.Severity == SolarAssistantGatewayHealthSeverity.Critical);
    }

    [TestMethod]
    public void Evaluate_ReturnsWarnings_ForLowBatteryHighLoadAndHighDischarge()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 30, loadPower: 5000, batteryPower: 5000),
            Mqtt("connected", now.AddSeconds(-15)),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("warning");
        health.Alerts.Select(a => a.Code).Should().Contain(["battery-low", "load-high", "battery-discharge-high"]);
    }

    [TestMethod]
    public void Evaluate_DoesNotAlertOnMqtt_WhenDiscoveryIsDisabled()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = Options();
        options.EnableMqttDiscovery = false;

        var health = SolarAssistantGatewayHealthService.Evaluate(
            options,
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("disconnected", now.AddMinutes(-30)),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("healthy");
        health.Alerts.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HealthCheck_ReturnsUnhealthy_WhenGatewayStateIsCritical()
    {
        var healthCheck = new SolarAssistantGatewayHealthCheck(new StaticGatewayHealthService(new SolarAssistantGatewayHealthSnapshot
        {
            State = "critical",
            Alerts =
            [
                new SolarAssistantGatewayHealthAlert
                {
                    Code = "rest-stale",
                    Severity = SolarAssistantGatewayHealthSeverity.Critical,
                    Message = "REST snapshot is stale.",
                }
            ],
        }));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("rest-stale");
        result.Data["state"].Should().Be("critical");
    }

    [TestMethod]
    public async Task HealthCheck_ReturnsDegraded_WhenGatewayStateIsWarning()
    {
        var healthCheck = new SolarAssistantGatewayHealthCheck(new StaticGatewayHealthService(new SolarAssistantGatewayHealthSnapshot
        {
            State = "warning",
            Alerts =
            [
                new SolarAssistantGatewayHealthAlert
                {
                    Code = "mqtt-stale",
                    Severity = SolarAssistantGatewayHealthSeverity.Warning,
                    Message = "MQTT discovery/state messages are stale.",
                }
            ],
        }));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [TestMethod]
    public void Evaluate_ReturnsTypedGatewayStatusPayload()
    {
        var now = DateTime.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var health = SolarAssistantGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-30),
            snapshotError: null,
            Snapshot(soc: 72, loadPower: 900, batteryPower: 200),
            Mqtt("connected", now.AddSeconds(-15)),
            pendingOutboxCount: 0,
            failedOutboxCount: 71,
            outboxError: null,
            now);

        var payload = SolarAssistantGatewayHealthService.CreatePayload(
            Options(),
            lastSnapshotAtUtc: now.AddSeconds(-30),
            snapshotError: null,
            restMetricCount: 124,
            mqttInventory: new SolarAssistantMqttInventory
            {
                ConnectionState = "connected",
                LastMessageAtUtc = now.AddSeconds(-15),
                EntityCount = 48,
                StateTopicCount = 42,
                CommandTopicCount = 14,
            },
            pendingOutboxCount: 0,
            failedOutboxCount: 71,
            lastSentAtUtc: now.AddSeconds(-5),
            lastBatchCount: 1,
            outboxError: null,
            health,
            now);

        payload.SourceId.Should().Be("solarassistant-total");
        payload.Identity.Domain.Should().Be(GatewayDomain.Power);
        payload.Health.State.Should().Be(GatewayHealthState.Warning);
        payload.Health.Alerts.Single().Code.Should().Be("outbox-failed");
        payload.Rest.State.Should().Be(GatewaySampleState.Live);
        payload.Mqtt!.State.Should().Be(GatewaySampleState.Live);
        payload.Outbox.FailedCount.Should().Be(71);
        payload.MqttCommandTopicCount.Should().Be(14);
    }

    private static SolarAssistantOptions Options() => new()
    {
        Host = "solarassistant.local",
        RestStaleAfterSeconds = 120,
        MqttStaleAfterSeconds = 120,
        OutboxPendingWarningCount = 10,
        OutboxFailedCriticalCount = 1,
        LowBatteryWarningPercent = 30,
        CriticalBatteryPercent = 15,
        HighLoadWarningW = 5000,
        BatteryDischargeWarningW = 5000,
        EnableMqttDiscovery = true,
    };

    private static PowerReadingPayload Snapshot(double soc, double loadPower, double batteryPower) => new()
    {
        RecordedAtUtc = DateTime.UtcNow,
        BatteryStateOfChargePercent = soc,
        LoadPowerW = loadPower,
        BatteryPowerW = batteryPower,
    };

    private static SolarAssistantMqttInventory Mqtt(string state, DateTime? lastMessageAtUtc) => new()
    {
        ConnectionState = state,
        LastMessageAtUtc = lastMessageAtUtc,
    };

    private sealed class StaticGatewayHealthService : IGatewayHealthSnapshotProvider
    {
        private readonly SolarAssistantGatewayHealthSnapshot _snapshot;

        public StaticGatewayHealthService(SolarAssistantGatewayHealthSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public SolarAssistantGatewayHealthSnapshot GetSnapshot(DateTime? nowUtc = null) => _snapshot;
    }
}

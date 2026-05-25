using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt.Health;

[TestClass]
public sealed class SmartShuntGatewayHealthServiceTests
{
    [TestMethod]
    public void Evaluate_ReturnsHealthy_WhenInputsAreFreshAndNormal()
    {
        var now = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SmartShuntGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-10),
            lastError: null,
            Snapshot(72),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("healthy");
        health.Alerts.Should().BeEmpty();
    }

    [TestMethod]
    public void Evaluate_ReturnsCritical_WhenSnapshotIsStale()
    {
        var now = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var health = SmartShuntGatewayHealthService.Evaluate(
            Options(),
            now.AddSeconds(-61),
            lastError: null,
            Snapshot(72),
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now);

        health.State.Should().Be("critical");
        health.Alerts.Should().ContainSingle(a => a.Code == "stale");
    }

    [TestMethod]
    public void Evaluate_ReturnsWarning_WhenAddressMissing()
    {
        var options = Options();
        options.Address = string.Empty;

        var health = SmartShuntGatewayHealthService.Evaluate(
            options,
            lastSnapshotAtUtc: null,
            lastError: null,
            snapshot: null,
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now: DateTime.UtcNow);

        health.State.Should().Be("warning");
        health.Alerts.Should().ContainSingle(a => a.Code == "address-missing");
    }

    [TestMethod]
    public void Evaluate_ReturnsCritical_ForOutboxFailureAndLowBattery()
    {
        var health = SmartShuntGatewayHealthService.Evaluate(
            Options(),
            DateTime.UtcNow,
            lastError: null,
            Snapshot(10),
            pendingOutboxCount: 0,
            failedOutboxCount: 1,
            outboxError: null,
            now: DateTime.UtcNow);

        health.State.Should().Be("critical");
        health.Alerts.Select(a => a.Code).Should().Contain(["outbox-failed", "battery-critical"]);
    }

    [TestMethod]
    public void Evaluate_DoesNotTreatZeroSocAsCritical_WhenOtherLiveValuesLookValid()
    {
        var health = SmartShuntGatewayHealthService.Evaluate(
            Options(),
            DateTime.UtcNow,
            lastError: null,
            new SmartShuntDeviceSnapshot
            {
                RecordedAtUtc = DateTime.UtcNow,
                StateOfChargePercent = 0,
                VoltageV = 53.75,
                CurrentA = -22.8,
                PowerW = -1229,
            },
            pendingOutboxCount: 0,
            failedOutboxCount: 0,
            outboxError: null,
            now: DateTime.UtcNow);

        health.Alerts.Select(a => a.Code).Should().NotContain(["battery-critical", "battery-low"]);
    }

    [TestMethod]
    public async Task HealthCheck_ReturnsDegraded_WhenGatewayStateIsWarning()
    {
        var healthCheck = new SmartShuntGatewayHealthCheck(new StaticProvider(new SmartShuntGatewayHealthSnapshot
        {
            State = "warning",
            Alerts =
            [
                new SmartShuntGatewayHealthAlert
                {
                    Code = "outbox-backlog",
                    Severity = SmartShuntGatewayHealthSeverity.Warning,
                    Message = "10 outbox record(s) are pending."
                }
            ]
        }));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["state"].Should().Be("warning");
    }

    private static SmartShuntOptions Options() => new()
    {
        Address = "E2:21:F0:89:A7:C0",
        SampleStaleAfterSeconds = 60,
        OutboxPendingWarningCount = 10,
        OutboxFailedCriticalCount = 1,
        LowBatteryWarningPercent = 30,
        CriticalBatteryPercent = 15,
    };

    private static SmartShuntDeviceSnapshot Snapshot(double soc) => new()
    {
        RecordedAtUtc = DateTime.UtcNow,
        StateOfChargePercent = soc,
        VoltageV = 53.7,
        CurrentA = -20,
        PowerW = -1000,
    };

    private sealed class StaticProvider(SmartShuntGatewayHealthSnapshot snapshot) : ISmartShuntGatewayHealthSnapshotProvider
    {
        public SmartShuntGatewayHealthSnapshot GetSnapshot(DateTime? nowUtc = null) => snapshot;
    }
}

using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class PowerSystemSnapshotTests
{
    [TestMethod]
    public void PowerSystemSnapshot_SupportsSourceProvenancePerMetric()
    {
        var recordedAtUtc = new DateTime(2026, 5, 27, 18, 15, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: recordedAtUtc,
            Pv: new PowerSystemPvSnapshot(
                PowerW: new SourcedValue<double>(1234.5, PowerMetricSource.SolarAssistant, recordedAtUtc, "solarassistant-total")),
            Battery: new PowerSystemBatterySnapshot(
                StateOfChargePercent: new SourcedValue<double>(89, PowerMetricSource.JkBms, recordedAtUtc, "jkbms", Confidence: "preferred"),
                PowerW: new SourcedValue<double>(420, PowerMetricSource.VictronSmartShunt, recordedAtUtc, "smartshunt-lifepo4")));

        snapshot.Pv!.PowerW!.Source.Should().Be(PowerMetricSource.SolarAssistant);
        snapshot.Battery!.StateOfChargePercent!.Source.Should().Be(PowerMetricSource.JkBms);
        snapshot.Battery.PowerW!.Source.Should().Be(PowerMetricSource.VictronSmartShunt);
    }

    [TestMethod]
    public void PowerSystemSnapshot_RoundTripsThroughJson()
    {
        var recordedAtUtc = new DateTime(2026, 5, 27, 18, 20, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: recordedAtUtc,
            Ac: new PowerSystemAcSnapshot(
                LoadPowerW: new SourcedValue<double>(875.25, PowerMetricSource.SolarAssistant, recordedAtUtc, "solarassistant-total"),
                InverterMode: new SourcedValue<string>("Battery", PowerMetricSource.SolarAssistant, recordedAtUtc)),
            BatteryBanks:
            [
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-1a",
                    RecordedAtUtc: recordedAtUtc,
                    Source: PowerMetricSource.JkBms,
                    SourceId: "jkbms",
                    DeviceId: "C8:47:8C:E4:58:37",
                    VoltageV: new SourcedValue<double>(53.814, PowerMetricSource.JkBms, recordedAtUtc),
                    CurrentA: new SourcedValue<double>(7.88, PowerMetricSource.JkBms, recordedAtUtc),
                    DeltaCellVoltageV: new SourcedValue<double>(0.003, PowerMetricSource.JkBms, recordedAtUtc))
            ]);

        var json = JsonSerializer.Serialize(snapshot);
        var result = JsonSerializer.Deserialize<PowerSystemSnapshot>(json);

        result.Should().NotBeNull();
        result!.ObservedAtUtc.Should().Be(recordedAtUtc);
        result.Ac!.LoadPowerW!.Value.Should().Be(875.25);
        result.Ac.InverterMode!.Value.Should().Be("Battery");
        result.BatteryBanks.Should().ContainSingle();
        result.BatteryBanks![0].BankId.Should().Be("bank-1a");
        result.BatteryBanks[0].DeltaCellVoltageV!.Value.Should().Be(0.003);
    }
}

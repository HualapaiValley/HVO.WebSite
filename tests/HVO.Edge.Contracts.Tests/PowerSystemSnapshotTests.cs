using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class PowerSystemSnapshotTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [TestMethod]
    public void PowerSystemSnapshot_SupportsSourceProvenancePerMetric()
    {
        var recordedAtUtc = new DateTime(2026, 5, 27, 18, 15, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: recordedAtUtc,
            Pv: new PowerSystemPvSnapshot(
                PowerW: new SourcedValue<double>(1234.5, PowerMetricSource.SolarAssistant, recordedAtUtc, "solarassistant-total"),
                Trackers:
                [
                    new PowerSystemPvTrackerSnapshot(
                        "solarassistant-total/mppt-1", "6500EX MPPT 1", "solarassistant-total", "inverter_1",
                        recordedAtUtc, PowerMetricSource.SolarAssistant, 336.5, 2.7, 934,
                        PowerObservationProvenance.Direct, "source-direct"),
                ],
                ExpectedTrackerCount: 3,
                ReportedTrackerCount: 1),
            Battery: new PowerSystemBatterySnapshot(
                StateOfChargePercent: new SourcedValue<double>(89, PowerMetricSource.JkBms, recordedAtUtc, "jkbms", Confidence: "preferred"),
                PowerW: new SourcedValue<double>(420, PowerMetricSource.VictronSmartShunt, recordedAtUtc, "smartshunt-lifepo4")));

        snapshot.Pv!.PowerW!.Source.Should().Be(PowerMetricSource.SolarAssistant);
        snapshot.Pv.Trackers.Should().ContainSingle().Which.Provenance.Should().Be(PowerObservationProvenance.Direct);
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

        var json = JsonSerializer.Serialize(snapshot, WebJsonOptions);
        var result = JsonSerializer.Deserialize<PowerSystemSnapshot>(json, WebJsonOptions);

        result.Should().NotBeNull();
        json.Should().Contain("\"loadPowerW\"");
        json.Should().Contain("\"source\":\"SolarAssistant\"");
        result!.ObservedAtUtc.Should().Be(recordedAtUtc);
        result.Ac!.LoadPowerW!.Value.Should().Be(875.25);
        result.Ac.InverterMode!.Value.Should().Be("Battery");
        result.BatteryBanks.Should().ContainSingle();
        result.BatteryBanks![0].BankId.Should().Be("bank-1a");
        result.BatteryBanks[0].DeltaCellVoltageV!.Value.Should().Be(0.003);
    }

    [TestMethod]
    public void PowerSystemSnapshot_AddsObservationsWithoutChangingLegacyShapeWhenAbsent()
    {
        var observedAtUtc = new DateTime(2026, 8, 9, 5, 45, 0, DateTimeKind.Utc);
        var legacySnapshot = new PowerSystemSnapshot(observedAtUtc);
        var observation = new PowerBatteryObservation(
            SourceId: "eg4-6500ex-east",
            DeviceId: "inverter-east",
            Source: PowerMetricSource.Eg46500Ex,
            Role: PowerMeasurementRole.InverterBranch,
            MeasurementPoint: "inverter-east-battery",
            ObservedAtUtc: observedAtUtc,
            VoltageV: 53.2,
            CurrentA: 12.5,
            PowerW: 665,
            StateOfChargePercent: 84);
        var snapshot = legacySnapshot with { BatteryObservations = [observation] };

        var legacyJson = JsonSerializer.Serialize(legacySnapshot, WebJsonOptions);
        var json = JsonSerializer.Serialize(snapshot, WebJsonOptions);
        var result = JsonSerializer.Deserialize<PowerSystemSnapshot>(json, WebJsonOptions);

        legacyJson.Should().NotContain("batteryObservations");
        json.Should().Contain("\"batteryObservations\"");
        result!.BatteryObservations.Should().ContainSingle();
        result.BatteryObservations![0].Role.Should().Be(PowerMeasurementRole.InverterBranch);
        result.BatteryObservations[0].StateOfChargePercent.Should().Be(84);
    }
}

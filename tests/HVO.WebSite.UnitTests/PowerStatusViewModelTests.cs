using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerStatusViewModelTests
{
    [TestMethod]
    public void FromSnapshot_FormatsLivePowerValuesAndSources()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            Ac: new PowerSystemAcSnapshot(
                LoadPowerW: Value(474d, PowerMetricSource.Eg46500Ex, observedAt),
                GridPowerW: Value(0d, PowerMetricSource.Eg46500Ex, observedAt),
                GridFlowDirection: Value(PowerFlowDirection.Idle, PowerMetricSource.Eg46500Ex, observedAt),
                InverterMode: new SourcedValue<string>("Battery", PowerMetricSource.Eg46500Ex, observedAt)),
            Pv: new PowerSystemPvSnapshot(Value(3098d, PowerMetricSource.Derived, observedAt)),
            Battery: new PowerSystemBatterySnapshot(
                StateOfChargePercent: Value(100d, PowerMetricSource.VictronSmartShunt, observedAt),
                PowerW: Value(2700d, PowerMetricSource.VictronSmartShunt, observedAt),
                FlowDirection: Value(PowerFlowDirection.Discharging, PowerMetricSource.VictronSmartShunt, observedAt),
                BankCount: Value(7, PowerMetricSource.JkBms, observedAt),
                HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
            BatteryBanks:
            [
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-2a",
                    RecordedAtUtc: observedAt.AddMinutes(-45),
                    Source: PowerMetricSource.JkBms,
                    StateOfChargePercent: Value(91d, PowerMetricSource.JkBms, observedAt),
                    VoltageV: Value(54.412d, PowerMetricSource.JkBms, observedAt),
                    CurrentA: Value(-3.24d, PowerMetricSource.JkBms, observedAt),
                    DeltaCellVoltageV: Value(0.004d, PowerMetricSource.JkBms, observedAt),
                    HasAlarms: Value(false, PowerMetricSource.JkBms, observedAt)),
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-1a",
                    RecordedAtUtc: observedAt.AddMinutes(-4),
                    Source: PowerMetricSource.JkBms,
                    StateOfChargePercent: Value(90d, PowerMetricSource.JkBms, observedAt),
                    VoltageV: Value(54.037d, PowerMetricSource.JkBms, observedAt),
                    CurrentA: Value(1.2d, PowerMetricSource.JkBms, observedAt),
                    PowerW: Value(64.84d, PowerMetricSource.JkBms, observedAt),
                    StateOfHealthPercent: Value(98d, PowerMetricSource.JkBms, observedAt),
                    MinCellVoltageV: Value(3.371d, PowerMetricSource.JkBms, observedAt),
                    MaxCellVoltageV: Value(3.374d, PowerMetricSource.JkBms, observedAt),
                    DeltaCellVoltageV: Value(0.003d, PowerMetricSource.JkBms, observedAt),
                    AverageCellVoltageV: Value(3.372d, PowerMetricSource.JkBms, observedAt),
                    BatteryTemperature1C: Value(24.5d, PowerMetricSource.JkBms, observedAt),
                    BatteryTemperature2C: Value(25.1d, PowerMetricSource.JkBms, observedAt),
                    PowerTubeTemperatureC: Value(31.2d, PowerMetricSource.JkBms, observedAt),
                    BalancingActive: Value(true, PowerMetricSource.JkBms, observedAt),
                    BalancingCurrentA: Value(0.12d, PowerMetricSource.JkBms, observedAt),
                    HasAlarms: Value(true, PowerMetricSource.JkBms, observedAt)),
            ]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.PvPower.Should().Be("3098 W");
        model.LoadPower.Should().Be("474 W");
        model.BatteryPower.Should().Be("-2700 W");
        model.BatteryFlow.Should().Be("Discharging");
        model.BatteryStateOfCharge.Should().Be("100%");
        model.BatterySource.Should().Be("SmartShunt");
        model.BatterySocSource.Should().Be("SmartShunt");
        model.InverterMode.Should().Be("Battery");
        model.BatteryBankCount.Should().Be("7 banks");
        model.BatteryAlarmState.Should().Be("No alarms");
        model.BatteryFreshnessState.Should().Be("1 stale bank");
        model.BatteryBanks.Should().HaveCount(2);
        model.BatteryBanks[0].BankId.Should().Be("bank-1a");
        model.BatteryBanks[0].HeadingId.Should().Be("power-bank-1");
        model.BatteryBanks[0].Seen.Should().Be("4 min ago");
        model.BatteryBanks[0].StateOfCharge.Should().Be("90%");
        model.BatteryBanks[0].Voltage.Should().Be("54.04 V");
        model.BatteryBanks[0].Current.Should().Be("+1.2 A");
        model.BatteryBanks[0].Power.Should().Be("+65 W");
        model.BatteryBanks[0].StateOfHealth.Should().Be("98%");
        model.BatteryBanks[0].CellRange.Should().Be("3.371 V to 3.374 V");
        model.BatteryBanks[0].AverageCellVoltage.Should().Be("3.372 V");
        model.BatteryBanks[0].DeltaCellVoltage.Should().Be("3 mV");
        model.BatteryBanks[0].Temperatures.Should().Be("B1 24.5 °C; B2 25.1 °C; Power 31.2 °C");
        model.BatteryBanks[0].Balancing.Should().Be("Active (+0.1 A)");
        model.BatteryBanks[0].FreshnessStatus.Should().Be("fresh");
        model.BatteryBanks[0].AlarmState.Should().Be("Active alarm");
        model.BatteryBanks[0].IsAlarmed.Should().BeTrue();
        model.BatteryBanks[1].BankId.Should().Be("bank-2a");
        model.BatteryBanks[1].HeadingId.Should().Be("power-bank-2");
        model.BatteryBanks[1].Seen.Should().Be("45 min ago");
        model.BatteryBanks[1].FreshnessStatus.Should().Be("stale");
        model.SnapshotState.Should().Be("Live");
        model.BatteryObservations.Should().BeEmpty();
    }

    [TestMethod]
    public void FromSnapshot_FormatsAgingBankFreshnessState()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            BatteryBanks:
            [
                new PowerSystemBatteryBankSnapshot("bank-1a", observedAt.AddMinutes(-7), PowerMetricSource.JkBms),
                new PowerSystemBatteryBankSnapshot("bank-2a", observedAt.AddMinutes(-2), PowerMetricSource.JkBms),
            ]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.BatteryFreshnessState.Should().Be("1 aging bank");
        model.BatteryBanks[0].FreshnessStatus.Should().Be("warning");
        model.BatteryBanks[1].FreshnessStatus.Should().Be("fresh");
    }

    [TestMethod]
    public void FromSnapshot_ReturnsWaitingModelWhenSnapshotIsMissing()
    {
        var model = PowerStatusViewModel.FromSnapshot(null);

        model.Should().Be(PowerStatusViewModel.Empty);
    }

    [TestMethod]
    public void FromSnapshot_TruncatesBankFreshnessBelowHourBoundary()
    {
        var observedAt = new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            BatteryBanks:
            [
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-1a",
                    RecordedAtUtc: observedAt.AddMinutes(-59).AddSeconds(-45),
                    Source: PowerMetricSource.JkBms),
                new PowerSystemBatteryBankSnapshot(
                    BankId: "bank-2a",
                    RecordedAtUtc: observedAt.AddHours(-2).AddMinutes(-30),
                    Source: PowerMetricSource.JkBms),
            ]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.BatteryBanks[0].Seen.Should().Be("59 min ago");
        model.BatteryBanks[1].Seen.Should().Be("2 hr ago");
    }

    [TestMethod]
    public void FromSnapshot_FormatsEveryBatteryRoleSelectionFreshnessAndProvenance()
    {
        var observedAt = new DateTime(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc);
        var smartShunt = Observation("smartshunt-main", "smartshunt", PowerMetricSource.VictronSmartShunt,
            PowerMeasurementRole.BusNet, observedAt.AddSeconds(-10), voltage: 54.2, current: -20, power: -1084, soc: 93);
        var inverterA = Observation("eg4-inverter-a", "inverter-a", PowerMetricSource.Eg46500Ex,
            PowerMeasurementRole.InverterBranch, observedAt.AddSeconds(-30), voltage: 54.4, current: -8, power: -435,
            provenance: PowerObservationProvenance.Derived,
            confidence: "PI30 voltage/SOC direct; current=discharge-charge; power=voltage*current",
            inputs: [new PowerObservationInput("eg4-inverter-a", observedAt.AddSeconds(-30), "inverter-a")]);
        var inverterB = Observation("eg4-inverter-b", "inverter-b", PowerMetricSource.Eg46500Ex,
            PowerMeasurementRole.InverterBranch, observedAt.AddSeconds(-40), voltage: 54.3, current: 3, power: 163);
        var mpptA = Observation("eg4-mppt-a", "controller-a", PowerMetricSource.Eg4Mppt10048Hv,
            PowerMeasurementRole.ChargeControllerBranch, observedAt.AddSeconds(-181), voltage: 54.5, current: -10, power: -545);
        var mpptB = Observation("eg4-mppt-b", "controller-b", PowerMetricSource.Eg4Mppt10048Hv,
            PowerMeasurementRole.ChargeControllerBranch, observedAt.AddSeconds(-25), voltage: 54.4, current: -9, power: -490);
        var batteryPack = Observation("jk-bms-1", "bank-1", PowerMetricSource.JkBms,
            PowerMeasurementRole.BatteryPack, observedAt.AddSeconds(-15), voltage: 54.3, current: 5, power: 272, soc: 88);
        var derived = new PowerBatteryObservation(
            "derived-6500ex-branch-sum", "all-inverters", PowerMetricSource.Derived,
            PowerMeasurementRole.DerivedAggregate, "6500ex-branch-sum", observedAt.AddSeconds(-30),
            CurrentA: -5, PowerW: -272, Provenance: PowerObservationProvenance.Derived,
            Inputs:
            [
                new PowerObservationInput("eg4-inverter-a", observedAt.AddSeconds(-30), "inverter-a"),
                new PowerObservationInput("eg4-inverter-b", observedAt.AddSeconds(-40), "inverter-b"),
            ]);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            Battery: new PowerSystemBatterySnapshot(
                StateOfChargePercent: Value(93d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt"),
                VoltageV: Value(54.2d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt"),
                CurrentA: Value(-20d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt"),
                PowerW: Value(-1084d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt")),
            BatteryBanks:
            [
                new PowerSystemBatteryBankSnapshot(
                    "bank-1", observedAt.AddSeconds(-15), PowerMetricSource.JkBms,
                    CurrentA: Value(-5d, PowerMetricSource.JkBms, observedAt),
                    PowerW: Value(-272d, PowerMetricSource.JkBms, observedAt)),
            ],
            Notes: ["Branch difference may include additional DC loads."],
            BatteryObservations: [smartShunt, inverterA, inverterB, mpptA, mpptB, batteryPack, derived]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.BatterySource.Should().Be("SmartShunt (smartshunt-main)");
        model.BatterySocSource.Should().Be("SmartShunt (smartshunt-main)");
        model.BatteryObservations.Should().HaveCount(7);
        model.BatteryObservations.Count(row => row.Role == "Inverter branch").Should().Be(2);
        model.BatteryObservations.Count(row => row.Role == "Charge-controller branch").Should().Be(2);
        var bus = model.BatteryObservations.Single(row => row.SourceId == "smartshunt-main");
        bus.Flow.Should().Be("Charging (into battery)");
        bus.Current.Should().Be("+20.0 A");
        bus.Power.Should().Be("+1084 W");
        bus.SelectionLabels.Should().Equal("Preferred bus: voltage, current, power", "Preferred SOC");
        bus.Provenance.Should().Be("Direct");
        var dischargingBranch = model.BatteryObservations.Single(row => row.SourceId == "eg4-inverter-b");
        dischargingBranch.Flow.Should().Be("Discharging (out of battery)");
        dischargingBranch.SelectionLabels.Should().Equal("Comparison only");
        model.BatteryObservations.Single(row => row.SourceId == "eg4-inverter-a").ProvenanceDetail.Should().Be(
            "PI30 voltage/SOC direct; current=discharge-charge; power=voltage*current; Inputs: eg4-inverter-a/inverter-a");
        var staleController = model.BatteryObservations.Single(row => row.SourceId == "eg4-mppt-a");
        staleController.FreshnessStatus.Should().Be("stale");
        var pack = model.BatteryObservations.Single(row => row.SourceId == "jk-bms-1");
        pack.Flow.Should().Be("Discharging (out of battery)");
        pack.Current.Should().Be("-5.0 A");
        model.BatteryBanks.Should().ContainSingle().Which.Current.Should().Be("-5.0 A");
        var derivedRow = model.BatteryObservations.Single(row => row.SourceId == "derived-6500ex-branch-sum");
        derivedRow.Role.Should().Be("Derived aggregate");
        derivedRow.Provenance.Should().Be("Derived");
        derivedRow.ProvenanceDetail.Should().Be("Inputs: eg4-inverter-a/inverter-a, eg4-inverter-b/inverter-b");
        model.BatteryObservationNotes.Should().Equal("Branch difference may include additional DC loads.");
    }

    [TestMethod]
    public void FromSnapshot_LabelsJkBmsSocFallbackIndependentlyFromSmartShuntBus()
    {
        var observedAt = new DateTime(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            Battery: new PowerSystemBatterySnapshot(
                StateOfChargePercent: Value(87d, PowerMetricSource.JkBms, observedAt, "jk-bms-1", "bank-1"),
                PowerW: Value(500d, PowerMetricSource.VictronSmartShunt, observedAt, "smartshunt-main", "smartshunt")),
            BatteryObservations:
            [
                Observation("smartshunt-main", "smartshunt", PowerMetricSource.VictronSmartShunt,
                    PowerMeasurementRole.BusNet, observedAt, power: 500),
                Observation("jk-bms-1", "bank-1", PowerMetricSource.JkBms,
                    PowerMeasurementRole.BatteryPack, observedAt, soc: 87),
            ]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.BatteryObservations.Single(row => row.SourceId == "smartshunt-main").SelectionLabels
            .Should().Equal("Preferred bus: power");
        model.BatteryObservations.Single(row => row.SourceId == "jk-bms-1").SelectionLabels
            .Should().Equal("Fallback SOC");
    }

    [TestMethod]
    public void FromSnapshot_MarksFutureAndNonFiniteObservationsInvalid()
    {
        var observedAt = new DateTime(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            ObservedAtUtc: observedAt,
            BatteryObservations:
            [
                Observation("future", "future", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch,
                    observedAt.AddMinutes(1), power: 100),
                Observation("invalid", "invalid", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch,
                    observedAt, current: double.NaN),
            ]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.BatteryObservations.Should().OnlyContain(row => row.FreshnessStatus == "invalid");
        model.BatteryObservations.Single(row => row.SourceId == "future").Freshness.Should().Be("Future timestamp");
        model.BatteryObservations.Single(row => row.SourceId == "invalid").Current.Should().Be("--");
    }

    [TestMethod]
    public void FromSnapshot_UsesOldestDerivedInputForFreshness()
    {
        var observedAt = new DateTime(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc);
        var derived = new PowerBatteryObservation(
            "derived-6500ex-branch-sum", "all-inverters", PowerMetricSource.Derived,
            PowerMeasurementRole.DerivedAggregate, "6500ex-branch-sum", observedAt,
            PowerW: -500, Provenance: PowerObservationProvenance.Derived,
            Inputs:
            [
                new PowerObservationInput("eg4-inverter-a", observedAt, "inverter-a"),
                new PowerObservationInput("eg4-inverter-b", observedAt.AddSeconds(-181), "inverter-b"),
            ]);
        var snapshot = new PowerSystemSnapshot(observedAt, BatteryObservations: [derived]);

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.BatteryObservations.Should().ContainSingle().Which.FreshnessStatus.Should().Be("stale");
    }

    [TestMethod]
    public void FromSnapshot_ProjectsIndependentPvInputsAndAggregateCompleteness()
    {
        var observedAt = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc);
        var snapshot = new PowerSystemSnapshot(
            observedAt,
            Pv: new PowerSystemPvSnapshot(
                new SourcedValue<double>(3000, PowerMetricSource.Derived, observedAt, Confidence: "complete tracker set (3/3)"),
                [
                    new PowerSystemPvTrackerSnapshot("eg4-6500ex-a/mppt-1", "MPPT 1", "eg4-6500ex-a", "inverter", observedAt.AddSeconds(-5), PowerMetricSource.Eg46500Ex, 330, 4, 1320, PowerObservationProvenance.Direct, "direct registers"),
                    new PowerSystemPvTrackerSnapshot("eg4-6500ex-a/mppt-2", "MPPT 2", "eg4-6500ex-a", "inverter", observedAt.AddSeconds(-5), PowerMetricSource.Eg46500Ex, 380, 3, 1140, PowerObservationProvenance.Direct, "direct registers"),
                    new PowerSystemPvTrackerSnapshot("eg4-mppt100-48hv-a/mppt-1", "External MPPT", "eg4-mppt100-48hv-a", "controller", observedAt.AddSeconds(-10), PowerMetricSource.Eg4Mppt10048Hv, 400, 1.35, 540, PowerObservationProvenance.Direct, "direct registers"),
                ],
                ExpectedTrackerCount: 3,
                ReportedTrackerCount: 3));

        var model = PowerStatusViewModel.FromSnapshot(snapshot);

        model.PvCompleteness.Should().Be("3 of 3 inputs");
        model.PvAggregateSource.Should().Be("Derived; complete tracker set (3/3)");
        model.PvTrackers.Should().HaveCount(3);
        model.PvTrackers.Single(tracker => tracker.TrackerId == "eg4-mppt100-48hv-a/mppt-1")
            .Should().Match<PowerStatusPvTrackerViewModel>(tracker =>
                tracker.Power == "540 W" && tracker.Voltage == "400.00 V" && tracker.Current == "1.4 A" && tracker.Provenance == "Direct");
    }

    private static PowerBatteryObservation Observation(
        string sourceId,
        string deviceId,
        PowerMetricSource source,
        PowerMeasurementRole role,
        DateTime observedAt,
        double? voltage = null,
        double? current = null,
        double? power = null,
        double? soc = null,
        PowerObservationProvenance provenance = PowerObservationProvenance.Direct,
        string? confidence = null,
        IReadOnlyList<PowerObservationInput>? inputs = null)
        => new(sourceId, deviceId, source, role, role.ToString(), observedAt, voltage, current, power, soc, provenance, confidence, inputs);

    private static SourcedValue<T> Value<T>(
        T value,
        PowerMetricSource source,
        DateTime recordedAt,
        string? sourceId = null,
        string? deviceId = null)
        => new(value, source, recordedAt, sourceId, deviceId);
}

using FluentAssertions;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Configuration;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerSystemSnapshotComposerTests
{
    [TestMethod]
    public void Compose_ConvertsSmartShuntSourceNativeSignsExactlyOnce()
    {
        var now = DateTime.UtcNow;
        var mapped = HVO.Hardware.VictronSmartShunt.SmartShunt.SmartShuntPowerMapper.MapSummary(
            new HVO.Hardware.VictronSmartShunt.SmartShunt.SmartShuntLiveSample
            { RecordedAtUtc = now, VoltageV = 52, CurrentA = 10, PowerW = 520, StateOfChargePercent = 80 },
            new HVO.Hardware.VictronSmartShunt.Configuration.SmartShuntOptions { SourceId = "smartshunt-main", DeviceId = "battery" });
        var snapshot = PowerSystemSnapshotComposer.Compose([new PowerReading
        {
            SourceId = mapped.SourceId!, SourceSystem = mapped.SourceSystem, DeviceId = mapped.DeviceId, RecordedAt = mapped.RecordedAtUtc,
            BatteryVoltageV = mapped.BatteryVoltageV, BatteryCurrentA = mapped.BatteryCurrentA, BatteryPowerW = mapped.BatteryPowerW,
            BatteryStateOfChargePercent = mapped.BatteryStateOfChargePercent
        }], now);

        mapped.BatteryCurrentA.Should().Be(10);
        mapped.BatteryPowerW.Should().Be(520);
        snapshot.Battery!.CurrentA!.Value.Should().Be(-10);
        snapshot.Battery.PowerW!.Value.Should().Be(-520);
        snapshot.BatteryObservations!.Single(item => item.Source == PowerMetricSource.VictronSmartShunt).CurrentA.Should().Be(-10);
    }
    [TestMethod]
    public void Compose_UsesFreshDirectEg4InverterDetailForAcLoadOutputAndMode()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [], now,
            inverterDetails: [InverterDetail(now.AddSeconds(-10), loadPowerW: 875, mode: "Battery")]);

        snapshot.Ac!.LoadPowerW.Should().BeEquivalentTo(
            new SourcedValue<double>(875, PowerMetricSource.Eg46500Ex, now.AddSeconds(-10), "eg4-6500ex-a", "inverter-a"));
        snapshot.Ac.GridVoltageV!.Value.Should().Be(240);
        snapshot.Ac.GridFrequencyHz!.Value.Should().Be(60);
        snapshot.Ac.OutputVoltageV!.Value.Should().Be(120);
        snapshot.Ac.OutputFrequencyHz!.Value.Should().Be(59.9);
        snapshot.Ac.LoadPercent!.Value.Should().Be(19);
        snapshot.Ac.InverterMode!.Value.Should().Be("Battery");
        snapshot.Ac.GridPowerW.Should().BeNull();
    }

    [TestMethod]
    public void Compose_PrefersSmartShuntForBatteryBusTelemetry()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [
                SmartShunt(now, voltageV: 53.74, currentA: -20.72, powerW: -1113, soc: 0),
            ],
            now);

        snapshot.Battery!.VoltageV!.Source.Should().Be(PowerMetricSource.VictronSmartShunt);
        snapshot.Battery.VoltageV.Value.Should().Be(53.74);
        snapshot.Battery.CurrentA!.Value.Should().Be(20.72);
        snapshot.Battery.PowerW!.Value.Should().Be(1113);
        snapshot.Battery.FlowDirection!.Value.Should().Be(PowerFlowDirection.Discharging);
    }

    [TestMethod]
    public void Compose_UsesSmartShuntAsAuthoritativeSocSource()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [SmartShunt(now, voltageV: 53.74, currentA: 12.3, powerW: 661, soc: 91)],
            now);

        snapshot.Battery!.StateOfChargePercent!.Source.Should().Be(PowerMetricSource.VictronSmartShunt);
        snapshot.Battery.StateOfChargePercent.Value.Should().Be(91);
        snapshot.Battery.StateOfChargePercent.Confidence.Should().BeNull();
        snapshot.Battery.FlowDirection!.Value.Should().Be(PowerFlowDirection.Charging);
    }

    [TestMethod]
    public void Compose_AddsJkBmsBatteryBanksAndAggregateStatus()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [],
            now,
            [
                JkBmsReading(now.AddSeconds(-5), "bank-2a", alarmBitmask: 0, cellVoltagesMv: [3361, 3364, 3362]),
                JkBmsReading(now, "bank-1a", alarmBitmask: 4, cellVoltagesMv: [3350, 3353, 3355]),
            ]);

        snapshot.BatteryBanks.Should().HaveCount(2);
        snapshot.BatteryBanks![0].BankId.Should().Be("bank-1a");
        snapshot.BatteryBanks[0].Source.Should().Be(PowerMetricSource.JkBms);
        snapshot.BatteryBanks[0].VoltageV!.Value.Should().Be(53.81);
        snapshot.BatteryBanks[0].CurrentA!.Value.Should().Be(7.5);
        snapshot.BatteryBanks[0].PowerW!.Value.Should().Be(403.575);
        snapshot.BatteryBanks[0].MinCellVoltageV!.Value.Should().Be(3.35);
        snapshot.BatteryBanks[0].MaxCellVoltageV!.Value.Should().Be(3.355);
        snapshot.BatteryBanks[0].DeltaCellVoltageV!.Value.Should().Be(0.005);
        snapshot.BatteryBanks[0].AverageCellVoltageV!.Value.Should().BeApproximately(3.3526667, 0.000001);
        snapshot.BatteryBanks[0].HasAlarms!.Value.Should().BeTrue();
        snapshot.Battery!.BankCount!.Value.Should().Be(2);
        snapshot.Battery.BankCount.Source.Should().Be(PowerMetricSource.JkBms);
        snapshot.Battery.HasAlarms!.Value.Should().BeTrue();
        snapshot.Battery.StateOfChargePercent!.Source.Should().Be(PowerMetricSource.JkBms);
        snapshot.Battery.StateOfChargePercent.Value.Should().Be(91);
        snapshot.Battery.StateOfChargePercent.SourceId.Should().Be("C8:47:8C:E4:56:B0");
        snapshot.BatteryObservations!.Where(item => item.Source == PowerMetricSource.JkBms)
            .Should().AllSatisfy(item => item.CurrentA.Should().Be(-7.5));
    }

    [TestMethod]
    public void Compose_PreservesMultipleInverterBranchesAndBuildsAggregateOnly()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [Eg4(now, "6500-a", "eg4-6500ex", 10, 520), Eg4(now.AddSeconds(-2), "6500-b", "eg4-6500ex", 12, 624)], now);

        snapshot.BatteryObservations!.Count(item => item.Role == PowerMeasurementRole.InverterBranch).Should().Be(2);
        snapshot.BatteryObservations!.Where(item => item.Role == PowerMeasurementRole.InverterBranch)
            .Should().AllSatisfy(item =>
            {
                item.Provenance.Should().Be(PowerObservationProvenance.Derived);
                item.Confidence.Should().Be("PI30 voltage/SOC direct; current=discharge-charge; power=voltage*current");
                item.Inputs.Should().ContainSingle().Which.Should().Be(
                    new PowerObservationInput(item.SourceId, item.ObservedAtUtc, item.DeviceId));
            });
        var aggregate = snapshot.BatteryObservations!.Single(item => item.SourceId == "derived-6500ex-branch-sum");
        aggregate.Role.Should().Be(PowerMeasurementRole.DerivedAggregate);
        aggregate.CurrentA.Should().Be(22);
        aggregate.PowerW.Should().Be(1144);
        aggregate.Inputs!.Select(input => input.SourceId).Should().BeEquivalentTo("6500-a", "6500-b");
    }

    [TestMethod]
    public void Compose_DerivesResidualOnlyWhenEveryConfiguredMpptInputIsFreshAndAligned()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = new PowerCompositionOptions { EnabledMpptSourceIds = ["mppt-a", "mppt-b"] };
        var snapshot = PowerSystemSnapshotComposer.Compose(
            [SmartShunt(now, 52, -100, -5200, 80), Eg4(now.AddSeconds(-1), "mppt-a", "eg4-mppt100-48hv", -10, -520),
                Eg4(now.AddSeconds(-2), "mppt-b", "eg4-mppt100-48hv", -20, -1040)], now, options: options);

        var residual = snapshot.BatteryObservations!.Single(item => item.SourceId == "derived-inverter-side-residual");
        residual.CurrentA.Should().Be(130);
        residual.PowerW.Should().Be(6760);
        residual.Provenance.Should().Be(PowerObservationProvenance.Derived);
        residual.Inputs.Should().HaveCount(3);

        var incomplete = PowerSystemSnapshotComposer.Compose(
            [SmartShunt(now, 52, -100, -5200, 80), Eg4(now, "mppt-a", "eg4-mppt100-48hv", -10, -520)], now, options: options);
        incomplete.BatteryObservations.Should().NotContain(item => item.SourceId == "derived-inverter-side-residual");
    }

    [TestMethod]
    public void Compose_SuppressesStaleAndTimestampSkewedDerivations()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = new PowerCompositionOptions
        {
            SmartShuntFreshnessSeconds = 60,
            Eg4BranchFreshnessSeconds = 60,
            MaxDerivationSkewSeconds = 5,
            EnabledMpptSourceIds = ["mppt-a"],
        };
        var stale = PowerSystemSnapshotComposer.Compose(
            [SmartShunt(now.AddMinutes(-2), 52, -100, -5200, 80), Eg4(now, "mppt-a", "eg4-mppt100-48hv", -10, -520)], now, options: options);
        stale.BatteryObservations.Should().NotContain(item => item.Source == PowerMetricSource.VictronSmartShunt);

        var skewed = PowerSystemSnapshotComposer.Compose(
            [SmartShunt(now, 52, -100, -5200, 80), Eg4(now.AddSeconds(-10), "mppt-a", "eg4-mppt100-48hv", -10, -520)], now, options: options);
        skewed.BatteryObservations.Should().NotContain(item => item.SourceId == "derived-inverter-side-residual");
    }

    [TestMethod]
    public void Compose_FutureInverterDetailDoesNotMaskPriorValidPayload()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var valid = InverterDetail(now.AddSeconds(-10), loadPowerW: 1300);
        var future = InverterDetail(now.AddMinutes(5), loadPowerW: 9999);

        var snapshot = PowerSystemSnapshotComposer.Compose([], now, inverterDetails: [future, valid]);

        snapshot.Ac!.LoadPowerW!.Value.Should().Be(1300);
    }

    [TestMethod]
    public void Compose_UsesValidatedSourcePreferenceAndSuppressesEmptyResidual()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var preferred = SmartShunt(now.AddSeconds(-10), 52, null, null, 80);
        preferred.SourceId = "smart-preferred";
        var newer = SmartShunt(now, 53, -100, -5300, 81);
        newer.SourceId = "smart-newer";
        var mppt = new PowerReading
        {
            SourceId = "mppt-a", SourceSystem = "eg4-mppt100-48hv", DeviceId = "mppt-a",
            RecordedAt = now, BatteryVoltageV = 52,
        };
        var options = new PowerCompositionOptions
        {
            PreferredSmartShuntSourceIds = ["smart-preferred"],
            EnabledMpptSourceIds = ["mppt-a"],
        };

        var snapshot = PowerSystemSnapshotComposer.Compose([preferred, newer, mppt], now, options: options);

        snapshot.Battery!.VoltageV!.SourceId.Should().Be("smart-preferred");
        snapshot.BatteryObservations.Should().NotContain(item => item.SourceId == "derived-inverter-side-residual");
    }

    [TestMethod]
    public void PowerCompositionOptionsValidator_RejectsInvalidOrDuplicateSourceIds()
    {
        var options = new PowerCompositionOptions
        {
            PreferredSmartShuntSourceIds = ["duplicate", "DUPLICATE"],
            EnabledMpptSourceIds = [" untrimmed"],
        };

        new PowerCompositionOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [TestMethod]
    public void Compose_DerivesPvAggregateFromAllThreeExpectedTrackers()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = PvOptions();

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [], now, options: options,
            mpptDetails:
            [
                MpptDetail(now.AddSeconds(-1), "eg4-6500ex-a", "eg4-6500ex", ("mppt-1", 500), ("mppt-2", 600)),
                MpptDetail(now, "eg4-mppt100-48hv-a", "eg4-mppt100-48hv", ("mppt-1", 700)),
            ]);

        snapshot.Pv!.PowerW!.Value.Should().Be(1800);
        snapshot.Pv.PowerW.Source.Should().Be(PowerMetricSource.Derived);
        snapshot.Pv.PowerW.SourceId.Should().Be("derived-pv-tracker-sum");
        snapshot.Pv.PowerW.Confidence.Should().Contain("3/3");
        snapshot.Pv.ExpectedTrackerCount.Should().Be(3);
        snapshot.Pv.ReportedTrackerCount.Should().Be(3);
        snapshot.Pv.Trackers.Should().HaveCount(3);
    }

    [TestMethod]
    public void Compose_DoesNotUseSolarAssistantFallbackWhenExpectedTrackerIsMissing()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [SolarAssistant(now, pvPowerW: 1900)], now, options: PvOptions(),
            mpptDetails: [MpptDetail(now, "eg4-6500ex-a", "eg4-6500ex", ("mppt-1", 500))]);

        snapshot.Pv!.PowerW.Should().BeNull();
        snapshot.Pv.Trackers.Should().ContainSingle();
        snapshot.Pv.ExpectedTrackerCount.Should().Be(3);
        snapshot.Pv.ReportedTrackerCount.Should().Be(1);
    }

    [TestMethod]
    public void Compose_ExcludesStaleAndFuturePvTrackers()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = PvOptions();
        options.Eg4BranchFreshnessSeconds = 60;
        options.MaxFutureClockSkewSeconds = 5;

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [], now, options: options,
            mpptDetails:
            [
                MpptDetail(now.AddSeconds(-61), "eg4-6500ex-a", "eg4-6500ex", ("mppt-1", 500)),
                MpptDetail(now.AddSeconds(6), "eg4-mppt100-48hv-a", "eg4-mppt100-48hv", ("mppt-1", 700)),
                MpptDetail(now, "eg4-6500ex-a", "eg4-6500ex", ("mppt-2", 600)),
            ]);

        snapshot.Pv!.Trackers.Should().ContainSingle()
            .Which.TrackerId.Should().Be("eg4-6500ex-a/mppt-2");
        snapshot.Pv.PowerW.Should().BeNull();
    }

    [TestMethod]
    public void Compose_DoesNotDerivePvAggregateWhenTrackersExceedSkewLimit()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = PvOptions();
        options.MaxDerivationSkewSeconds = 5;

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [], now, options: options,
            mpptDetails:
            [
                MpptDetail(now.AddSeconds(-10), "eg4-6500ex-a", "eg4-6500ex", ("mppt-1", 500), ("mppt-2", 600)),
                MpptDetail(now, "eg4-mppt100-48hv-a", "eg4-mppt100-48hv", ("mppt-1", 700)),
            ]);

        snapshot.Pv!.PowerW.Should().BeNull();
        snapshot.Pv.Trackers.Should().HaveCount(3);
    }

    [TestMethod]
    public void Compose_DeduplicatesPhysicalTrackerAndRejectsDuplicateFromAggregate()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [], now, options: PvOptions(),
            mpptDetails:
            [
                MpptDetail(now, "eg4-6500ex-a", "eg4-6500ex", ("mppt-1", 500), ("MPPT-1", 500), ("mppt-2", 600)),
                MpptDetail(now, "eg4-mppt100-48hv-a", "eg4-mppt100-48hv", ("mppt-1", 700)),
            ]);

        snapshot.Pv!.Trackers.Should().HaveCount(3);
        snapshot.Pv.PowerW.Should().BeNull();
    }

    [TestMethod]
    public void Compose_DoesNotDerivePvAggregateWhenExpectedTrackerPowerIsNull()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var detail = MpptDetail(now, "eg4-6500ex-a", "eg4-6500ex", ("mppt-1", 500), ("mppt-2", 600));
        var controller = MpptDetail(now, "eg4-mppt100-48hv-a", "eg4-mppt100-48hv", ("mppt-1", 700));
        controller = new PowerMpptDetailPayload
        {
            SourceId = controller.SourceId,
            SourceSystem = controller.SourceSystem,
            DeviceId = controller.DeviceId,
            RecordedAtUtc = controller.RecordedAtUtc,
            Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "mppt-1", PowerW = null }],
        };

        var snapshot = PowerSystemSnapshotComposer.Compose(
            [], now, options: PvOptions(), mpptDetails: [detail, controller]);

        snapshot.Pv!.PowerW.Should().BeNull();
        snapshot.Pv.Trackers.Should().HaveCount(3);
    }

    [TestMethod]
    public void Compose_MapsTrackerSourcesProvenanceAndConfidence()
    {
        var now = DateTime.Parse("2026-05-27T18:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var snapshot = PowerSystemSnapshotComposer.Compose([], now, options: PvOptions(), mpptDetails:
        [
            MpptDetail(now, "eg4-6500ex-a", "eg4-6500ex", ("mppt-1", 500), ("mppt-2", 600)),
            MpptDetail(now, "eg4-mppt100-48hv-a", "eg4-mppt100-48hv", ("mppt-1", 700)),
        ]);

        snapshot.Pv!.Trackers!.Single(tracker => tracker.TrackerId == "eg4-6500ex-a/mppt-1").Source
            .Should().Be(PowerMetricSource.Eg46500Ex);
        var controller = snapshot.Pv.Trackers!.Single(tracker => tracker.TrackerId == "eg4-mppt100-48hv-a/mppt-1");
        controller.Source.Should().Be(PowerMetricSource.Eg4Mppt10048Hv);
        controller.Provenance.Should().Be(PowerObservationProvenance.Direct);
        controller.Confidence.Should().Be("independent");
    }

    [TestMethod]
    public void PowerCompositionOptionsValidator_RequiresCompositeExpectedTrackerIds()
    {
        var options = new PowerCompositionOptions { ExpectedPvTrackerIds = ["not-composite"] };

        new PowerCompositionOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    private static PowerReading SolarAssistant(
        DateTime recordedAt,
        double? pvPowerW = null,
        double? loadPowerW = null,
        double? gridPowerW = null,
        double? batteryPowerW = null,
        double? voltageV = null,
        double? currentA = null,
        double? soc = null) => new()
        {
            Id = 1,
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAt = recordedAt,
            PvPowerW = pvPowerW,
            LoadPowerW = loadPowerW,
            GridPowerW = gridPowerW,
            BatteryPowerW = batteryPowerW,
            BatteryVoltageV = voltageV,
            BatteryCurrentA = currentA,
            BatteryStateOfChargePercent = soc,
            GridVoltageV = 240,
            GridFrequencyHz = 60,
            OutputVoltageV = 120,
            OutputFrequencyHz = 60,
        };

    private static PowerCompositionOptions PvOptions() => new()
    {
        ExpectedPvTrackerIds =
        [
            "eg4-6500ex-a/mppt-1",
            "eg4-6500ex-a/mppt-2",
            "eg4-mppt100-48hv-a/mppt-1",
        ],
    };

    private static PowerInverterDetailPayload InverterDetail(DateTime recordedAt, double loadPowerW, string mode = "Battery") => new()
    {
        SourceId = "eg4-6500ex-a",
        SourceSystem = "eg4-6500ex",
        DeviceId = "inverter-a",
        RecordedAtUtc = recordedAt,
        Ac = new PowerInverterAcDetail
        {
            InputVoltageV = 240,
            InputFrequencyHz = 60,
            OutputVoltageV = 120,
            OutputFrequencyHz = 59.9,
        },
        Load = new PowerInverterLoadDetail { LoadPowerW = loadPowerW },
        Operating = new PowerInverterOperatingDetail { Mode = mode, LoadPercentage = 19 },
    };

    private static PowerMpptDetailPayload MpptDetail(
        DateTime recordedAt,
        string sourceId,
        string sourceSystem,
        params (string TrackerId, double PowerW)[] trackers) => new()
        {
            SourceId = sourceId,
            SourceSystem = sourceSystem,
            DeviceId = sourceId,
            RecordedAtUtc = recordedAt,
            Trackers = trackers.Select(tracker => new PowerMpptTrackerDetail
            {
                TrackerId = tracker.TrackerId,
                Name = tracker.TrackerId,
                VoltageV = 120,
                CurrentA = tracker.PowerW / 120,
                PowerW = tracker.PowerW,
                Provenance = PowerObservationProvenance.Direct,
                Confidence = "independent",
            }).ToArray(),
        };

    private static PowerReading SmartShunt(
        DateTime recordedAt,
        double? voltageV,
        double? currentA,
        double? powerW,
        double? soc) => new()
        {
            Id = 2,
            SourceId = "smartshunt-main",
            SourceSystem = "victron-smartshunt",
            DeviceId = "smartshunt-lifepo4",
            RecordedAt = recordedAt,
            BatteryVoltageV = voltageV,
            BatteryCurrentA = currentA,
            BatteryPowerW = powerW,
            BatteryStateOfChargePercent = soc,
        };

    private static PowerReading Eg4(DateTime recordedAt, string sourceId, string sourceSystem, double currentA, double powerW) => new()
    {
        SourceId = sourceId,
        SourceSystem = sourceSystem,
        DeviceId = sourceId,
        RecordedAt = recordedAt,
        BatteryVoltageV = 52,
        BatteryCurrentA = currentA,
        BatteryPowerW = powerW,
    };

    private static BmsReading JkBmsReading(
        DateTime recordedAt,
        string alias,
        long alarmBitmask,
        int[] cellVoltagesMv) => new()
        {
            Id = alias == "bank-1a" ? 10 : 11,
            DeviceId = alias == "bank-1a" ? 1 : 2,
            Device = new BmsDevice
            {
                Id = alias == "bank-1a" ? 1 : 2,
                Address = alias == "bank-1a" ? "C8:47:8C:E4:56:B0" : "C8:47:8C:EC:1B:0F",
                Alias = alias,
            },
            RecordedAt = recordedAt,
            PackVoltageMv = 53810,
            CurrentMa = 7500,
            PowerWatts = 403.575,
            SocPercent = 91,
            SohPercent = 100,
            BatteryTemp1C = 22.1,
            BatteryTemp2C = 22.4,
            PowerTubeC = 23.6,
            BalancingActive = false,
            BalancingCurrentMa = 0,
            DeltaCellVoltageMv = 5,
            AlarmBitmask = alarmBitmask,
            CellVoltages = cellVoltagesMv
                .Select((voltage, index) => new BmsCellVoltage { CellIndex = (byte)(index + 1), VoltageMv = voltage })
                .ToArray(),
        };
}

using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt;

[TestClass]
public sealed class SmartShuntPowerMapperOverlayTests
{
    [TestMethod]
    public void ApplyOverlay_PrefersPrivateSocAndRemainingTime_WhenAvailable()
    {
        var now = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = new SmartShuntDeviceSnapshot
        {
            RecordedAtUtc = now,
            DataPath = "public",
            PublicSessionActive = true,
            StateOfChargePercent = 0,
            VoltageV = 53.7,
            CurrentA = -20,
            PowerW = -1000,
            ConsumedAh = -100,
            RemainingMinutes = null,
        };

        var merged = SmartShuntPowerMapper.ApplyOverlay(snapshot, new SmartShuntPrivateOverlay
        {
            RecordedAtUtc = now,
            StateOfChargePercent = 93.6,
            RemainingMinutes = 120,
            TotalChargeCycles = 21,
            FullDischarges = 1,
            CumulativeAhDrawn = -327399.7,
            MinBatteryVoltageV = 12.67,
            MaxBatteryVoltageV = 59.00,
            TimeSinceLastFullSeconds = 5001,
            Synchronizations = 616,
            LowVoltageAlarms = 0,
            HighVoltageAlarms = 0,
            MinStarterVoltageV = -0.01,
            MaxStarterVoltageV = 0,
            DischargedEnergyKwh = 17349.6,
            ChargedEnergyKwh = 18057.5,
            AlarmLowVoltageSetV = 46.00,
            AlarmLowVoltageClearV = 47.00,
            AlarmHighVoltageSetV = 60.00,
            AlarmHighVoltageClearV = 59.00,
            AlarmLowStarterSetV = 11.50,
            AlarmLowStarterClearV = 12.00,
            AlarmHighStarterSetV = 15.00,
            AlarmHighStarterClearV = 14.50,
            AlarmLowSocSetPercent = 20.0,
            AlarmLowSocClearPercent = 25.0,
            StreamingCounter = 12345,
            ChargeStatusCoarsePercent = 94,
            CurrentCoarseA = -12.3,
        });

        merged.DataPath.Should().Be("public+private");
        merged.StateOfChargePercent.Should().Be(93.6);
        merged.RemainingMinutes.Should().Be(120);
        merged.TotalChargeCycles.Should().Be(21);
        merged.FullDischarges.Should().Be(1);
        merged.CumulativeAhDrawn.Should().Be(-327399.7);
        merged.MinBatteryVoltageV.Should().Be(12.67);
        merged.MaxBatteryVoltageV.Should().Be(59.00);
        merged.TimeSinceLastFullSeconds.Should().Be(5001);
        merged.Synchronizations.Should().Be(616);
        merged.LowVoltageAlarms.Should().Be(0);
        merged.HighVoltageAlarms.Should().Be(0);
        merged.MinStarterVoltageV.Should().Be(-0.01);
        merged.MaxStarterVoltageV.Should().Be(0);
        merged.DischargedEnergyKwh.Should().Be(17349.6);
        merged.ChargedEnergyKwh.Should().Be(18057.5);
        merged.AlarmLowVoltageSetV.Should().Be(46.00);
        merged.AlarmLowVoltageClearV.Should().Be(47.00);
        merged.AlarmHighVoltageSetV.Should().Be(60.00);
        merged.AlarmHighVoltageClearV.Should().Be(59.00);
        merged.AlarmLowStarterSetV.Should().Be(11.50);
        merged.AlarmLowStarterClearV.Should().Be(12.00);
        merged.AlarmHighStarterSetV.Should().Be(15.00);
        merged.AlarmHighStarterClearV.Should().Be(14.50);
        merged.AlarmLowSocSetPercent.Should().Be(20.0);
        merged.AlarmLowSocClearPercent.Should().Be(25.0);
        merged.StreamingCounter.Should().Be(12345);
        merged.ChargeStatusCoarsePercent.Should().Be(94);
        merged.CurrentCoarseA.Should().Be(-12.3);
        merged.VoltageV.Should().Be(53.7);
    }

    [TestMethod]
    public void ApplyOverlay_DoesNotUseStaleOverlay_WhenPublicSnapshotLooksValid()
    {
        var now = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = new SmartShuntDeviceSnapshot
        {
            RecordedAtUtc = now,
            DataPath = "public",
            PublicSessionActive = true,
            StateOfChargePercent = 72.4,
            VoltageV = 53.7,
            CurrentA = -20,
            PowerW = -1000,
            RemainingMinutes = 90,
        };

        var merged = SmartShuntPowerMapper.ApplyOverlay(snapshot, new SmartShuntPrivateOverlay
        {
            RecordedAtUtc = now.AddHours(-2),
            StateOfChargePercent = 93.6,
            RemainingMinutes = 120,
            CumulativeAhDrawn = -327399.7,
        }, TimeSpan.FromMinutes(30));

        merged.DataPath.Should().Be("public");
        merged.StateOfChargePercent.Should().Be(72.4);
        merged.RemainingMinutes.Should().Be(90);
        merged.CumulativeAhDrawn.Should().BeNull();
    }

    [TestMethod]
    public void ApplyOverlay_StillUsesStaleOverlay_WhenPublicSocLooksInvalid()
    {
        var now = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var snapshot = new SmartShuntDeviceSnapshot
        {
            RecordedAtUtc = now,
            DataPath = "public",
            PublicSessionActive = true,
            StateOfChargePercent = 0,
            VoltageV = 53.7,
            CurrentA = -20,
            PowerW = -1000,
        };

        var merged = SmartShuntPowerMapper.ApplyOverlay(snapshot, new SmartShuntPrivateOverlay
        {
            RecordedAtUtc = now.AddHours(-2),
            StateOfChargePercent = 18.4,
            RemainingMinutes = 120,
        }, TimeSpan.FromMinutes(30));

        merged.DataPath.Should().Be("public+private");
        merged.StateOfChargePercent.Should().Be(18.4);
        merged.RemainingMinutes.Should().Be(120);
    }
}

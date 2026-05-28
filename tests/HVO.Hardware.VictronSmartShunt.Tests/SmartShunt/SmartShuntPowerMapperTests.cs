using FluentAssertions;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Tests.SmartShunt;

[TestClass]
public sealed class SmartShuntPowerMapperTests
{
    [TestMethod]
    public void MapLiveSample_MapsBatteryTelemetryIntoPowerPayload()
    {
        var recordedAt = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = new SmartShuntOptions
        {
            SourceId = "smartshunt-main",
            DeviceId = "smartshunt-lifepo4",
        };

        var payload = SmartShuntPowerMapper.MapLiveSample(new SmartShuntLiveSample
        {
            RecordedAtUtc = recordedAt,
            StateOfChargePercent = 92.1,
            VoltageV = 53.74,
            CurrentA = -20.72,
            PowerW = -1113,
            ConsumedAh = -162.0,
        }, options);

        payload.SourceId.Should().Be("smartshunt-main");
        payload.SourceSystem.Should().Be("victron-smartshunt");
        payload.DeviceId.Should().Be("smartshunt-lifepo4");
        payload.RecordedAtUtc.Should().Be(recordedAt);
        payload.BatteryStateOfChargePercent.Should().Be(92.1);
        payload.BatteryVoltageV.Should().Be(53.74);
        payload.BatteryCurrentA.Should().Be(-20.72);
        payload.BatteryPowerW.Should().Be(-1113);
        payload.SystemPowerW.Should().Be(-1113);
    }

    [TestMethod]
    public void MapLiveSample_UsesPrivateSocOverlay_WhenPublicSocLooksInvalid()
    {
        var recordedAt = DateTime.Parse("2026-05-24T20:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = new SmartShuntOptions
        {
            SourceId = "smartshunt-main",
            DeviceId = "smartshunt-lifepo4",
        };

        var payload = SmartShuntPowerMapper.MapLiveSample(new SmartShuntLiveSample
        {
            RecordedAtUtc = recordedAt,
            StateOfChargePercent = 0,
            VoltageV = 53.74,
            CurrentA = -20.72,
            PowerW = -1113,
            ConsumedAh = -162.0,
        }, options, new SmartShuntPrivateOverlay
        {
            RecordedAtUtc = recordedAt.AddHours(-2),
            StateOfChargePercent = 18.4,
        }, TimeSpan.FromMinutes(30));

        payload.BatteryStateOfChargePercent.Should().Be(18.4);
        payload.BatteryVoltageV.Should().Be(53.74);
        payload.BatteryCurrentA.Should().Be(-20.72);
    }
}

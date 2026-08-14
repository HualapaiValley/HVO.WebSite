using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Cwop;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Tests.Cwop;

[TestClass]
public sealed class CwopPacketFormatterTests
{
    [TestMethod]
    public void FormatLogin_UsesAprsIsContract()
    {
        CwopPacketFormatter.FormatLogin("DW4515", "-1", "HVO-Davis", "1.0")
            .Should().Be("user DW4515 pass -1 vers HVO-Davis 1.0");
    }

    [TestMethod]
    public void FormatPacket_MapsCompleteDavisObservation()
    {
        var packet = CwopPacketFormatter.FormatPacket("DW4515", new Loop2Packet
        {
            RecordedAtUtc = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc),
            WindDirectionDegrees = 360,
            WindGust10MinDirectionDegrees = 225,
            WindSpeed2MinAvgMph = 12.4,
            WindGust10MinMph = 20.6,
            OutsideTemperatureF = 72.4,
            HourRainInches = 0.12,
            Rain24HourInches = 0.87,
            DailyRainInches = 1.23,
            OutsideHumidityPercent = 100,
            BarometricPressureInHg = 29.92,
            SolarRadiationWm2 = 1234,
        }, Settings());

        packet.Should().Be("DW4515>APRS,TCPIP*:@141200z3507.40N/11434.07W_000/012g021t072r012p087P123h00b10132l234/A=004567");
    }

    [TestMethod]
    public void FormatPacket_EncodesMissingWeatherWithoutInventingValues()
    {
        var packet = CwopPacketFormatter.FormatPacket("DW4515", new Loop2Packet
        {
            RecordedAtUtc = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc),
        }, Settings());

        packet.Should().Contain("_.../...g...t...r...p...P...h..b...../A=004567");
        packet.Should().NotContain("L000");
    }

    [TestMethod]
    [DataRow(null, -114.0, 1000.0, "invalid-latitude")]
    [DataRow(91.0, -114.0, 1000.0, "invalid-latitude")]
    [DataRow(35.0, -181.0, 1000.0, "invalid-longitude")]
    [DataRow(35.0, -114.0, double.NaN, "invalid-elevation")]
    public void ValidatePosition_RejectsMissingOrInvalidValues(
        double? latitude,
        double? longitude,
        double? elevation,
        string expected)
    {
        CwopPacketFormatter.ValidatePosition(new StationSettings
        {
            LatitudeDegrees = latitude,
            LongitudeDegrees = longitude,
            AltitudeFeet = elevation,
        }).Should().Be(expected);
    }

    private static StationSettings Settings() => new()
    {
        LatitudeDegrees = 35.1234,
        LongitudeDegrees = -114.5678,
        AltitudeFeet = 4567,
    };
}

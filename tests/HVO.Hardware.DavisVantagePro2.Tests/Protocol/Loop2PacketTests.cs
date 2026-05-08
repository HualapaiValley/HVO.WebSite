using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;

namespace HVO.Hardware.DavisVantagePro2.Tests.Protocol;

[TestClass]
public class Loop2PacketTests
{
    // ── Happy-path field decoding ─────────────────────────────────────────────

    [TestMethod]
    public void Parse_ValidBuffer_ReturnsOutsideTemperature()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(outsideTempF: 72.5);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.OutsideTemperatureF.Should().BeApproximately(72.5, 0.1);
    }

    [TestMethod]
    public void Parse_ValidBuffer_ReturnsInsideTemperature()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(insideTempF: 68.0);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.InsideTemperatureF.Should().BeApproximately(68.0, 0.1);
    }

    [TestMethod]
    public void Parse_ValidBuffer_ReturnsOutsideHumidity()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(outsideHumidity: 55, insideHumidity: 45);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.OutsideHumidityPercent.Should().Be(55);
        packet.InsideHumidityPercent.Should().Be(45);
    }

    [TestMethod]
    public void Parse_ValidBuffer_ReturnsBarometricPressure()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(baroPressureInHg: 29.500);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.BarometricPressureInHg.Should().BeApproximately(29.500, 0.001);
    }

    [TestMethod]
    public void Parse_ValidBuffer_ReturnsWindSpeed()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(windSpeedMph: 12);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.WindSpeedMph.Should().Be(12.0);
    }

    [TestMethod]
    public void Parse_ValidBuffer_ReturnsWindDirection()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(windDirDeg: 270);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.WindDirectionDegrees.Should().Be(270.0);
    }

    [TestMethod]
    public void Parse_ValidBuffer_ReturnsDewPoint()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(dewPointF: 55.0);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.DewPointF.Should().Be(55.0);
    }

    [TestMethod]
    public void Parse_BarometricTrend_FallingFast_DecodesCorrectly()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[3] = unchecked((byte)(sbyte)(-2)); // falling fast
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.BarometricTrend.Should().Be(-2);
    }

    [TestMethod]
    public void Parse_BarometricTrend_Rising_DecodesCorrectly()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[3] = (byte)(sbyte)2; // rising fast
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.BarometricTrend.Should().Be(2);
    }

    [TestMethod]
    public void Parse_BarometricTrend_RawDavisValue_PreservesByte()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[3] = 20;
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.BarometricTrend.Should().Be(20);
    }

    [TestMethod]
    public void Parse_RecordedAtUtc_IsWithinTestWindow()
    {
        var before = DateTime.UtcNow;
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        var after = DateTime.UtcNow;
        packet.RecordedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── Rain decoding by bucket type ──────────────────────────────────────────

    [TestMethod]
    public void Parse_RainClicks_BucketType0_DecodesTo001InchUnits()
    {
        // 0.01-inch bucket: 10 clicks = 0.10 inches
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(rainClicks: 10);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.DailyRainInches.Should().BeApproximately(0.10, 0.001);
    }

    [TestMethod]
    public void Parse_ZeroRainClicks_ReturnsZeroInches()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes(rainClicks: 0);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.DailyRainInches.Should().BeApproximately(0.0, 0.001);
    }

    // ── Null / dash sentinel handling ─────────────────────────────────────────

    [TestMethod]
    public void Parse_DashSentinelOutsideTemp_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        // 0x7FFF in little-endian = [0xFF, 0x7F]
        buf[12] = 0xFF;
        buf[13] = 0x7F;
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.OutsideTemperatureF.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DashSentinelOutsideHumidity_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[33] = 0xFF;
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.OutsideHumidityPercent.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DashSentinelInsideHumidity_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[11] = 0xFF;
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.InsideHumidityPercent.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DashSentinelWindSpeed_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[14] = 0xFF;
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.WindSpeedMph.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DirectFahrenheitByteDash_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[35] = 0xFF;
        buf[36] = 0x00;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.HeatIndexF.Should().BeNull();
    }

    [TestMethod]
    public void Parse_WindSpeedLoop2_7fffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[18] = 0xFF;
        buf[19] = 0x7F;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.WindSpeed10MinAvgMph.Should().BeNull();
    }

    [TestMethod]
    public void Parse_WindDirection_FfffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[24] = 0xFF;
        buf[25] = 0xFF;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.WindGust10MinDirectionDegrees.Should().BeNull();
    }

    [TestMethod]
    public void Parse_SolarRadiationDash_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        // 0x7FFF already set by the builder — just verify the parser handles it
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.SolarRadiationWm2.Should().BeNull();
    }

    [TestMethod]
    public void Parse_UvIndexDash_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[43] = 0xFF; // already set by builder
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.UvIndex.Should().BeNull();
    }

    [TestMethod]
    public void Parse_StormRainDash_ReturnsNull()
    {
        // 0xFFFF in the storm rain field means no storm
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.StormRainInches.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DailyEtZero_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[56] = 0x00;
        buf[57] = 0x00;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.DailyEtInches.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DailyEtPositiveValue_DecodesCorrectly()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[56] = 0x85;
        buf[57] = 0x00;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.DailyEtInches.Should().BeApproximately(0.133, 0.001);
    }

    [TestMethod]
    public void Parse_BarometricPressureFfffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[7] = 0xFF;
        buf[8] = 0xFF;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.BarometricPressureInHg.Should().BeNull();
    }

    [TestMethod]
    public void Parse_RawPressureFfffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[65] = 0xFF;
        buf[66] = 0xFF;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.PressureRawInHg.Should().BeNull();
    }

    [TestMethod]
    public void Parse_AltimeterFfffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[69] = 0xFF;
        buf[70] = 0xFF;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.AltimeterInHg.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DewPointFfffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[30] = 0xFF;
        buf[31] = 0xFF;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.DewPointF.Should().BeNull();
    }

    [TestMethod]
    public void Parse_ThswPositiveValue_DecodesCorrectly()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[39] = 82;
        buf[40] = 0x00;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.ThswF.Should().Be(82);
    }

    [TestMethod]
    public void Parse_ThswFfffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[39] = 0xFF;
        buf[40] = 0xFF;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.ThswF.Should().BeNull();
    }

    [TestMethod]
    public void Parse_WindChillPositiveValue_DecodesCorrectly()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[37] = 61;
        buf[38] = 0x00;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.WindChillF.Should().Be(61);
    }

    [TestMethod]
    public void Parse_WindChillFfffSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[37] = 0xFF;
        buf[38] = 0xFF;

        var packet = Loop2Packet.Parse(buf, bucketType: 0);

        packet.WindChillF.Should().BeNull();
    }

    [TestMethod]
    public void Parse_Loop2RainFieldsFfffSentinel_ReturnNull()
    {
        byte[] rain15 = PacketBuilder.BuildLoop2DataBytes();
        rain15[52] = 0xFF;
        rain15[53] = 0xFF;

        byte[] hourRain = PacketBuilder.BuildLoop2DataBytes();
        hourRain[54] = 0xFF;
        hourRain[55] = 0xFF;

        byte[] rain24 = PacketBuilder.BuildLoop2DataBytes();
        rain24[58] = 0xFF;
        rain24[59] = 0xFF;

        Loop2Packet.Parse(rain15, bucketType: 0).Rain15MinInches.Should().BeNull();
        Loop2Packet.Parse(hourRain, bucketType: 0).HourRainInches.Should().BeNull();
        Loop2Packet.Parse(rain24, bucketType: 0).Rain24HourInches.Should().BeNull();
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_BufferTooShort_ThrowsDavisProtocolException()
    {
        byte[] shortBuf = new byte[50]; // need at least 95 bytes
        Action act = () => Loop2Packet.Parse(shortBuf, bucketType: 0);
        act.Should().Throw<DavisProtocolException>();
    }

    [TestMethod]
    public void Parse_InvalidLooHeader_ThrowsDavisProtocolException()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[0] = 0x00; // corrupt the 'L'
        Action act = () => Loop2Packet.Parse(buf, bucketType: 0);
        act.Should().Throw<DavisProtocolException>();
    }

    [TestMethod]
    public void Parse_WrongPacketType_ThrowsDavisUnknownPacketTypeException()
    {
        byte[] buf = PacketBuilder.BuildLoop2DataBytes();
        buf[4] = 0x02; // not LOOP1 (0x00) or LOOP2 (0x01) — truly unknown type
        Action act = () => Loop2Packet.Parse(buf, bucketType: 0);
        act.Should().Throw<DavisUnknownPacketTypeException>();
    }

    // ── LOOP1-specific fields ─────────────────────────────────────────────────

    [TestMethod]
    public void ParseLoop1_ConsoleBatteryVoltage_DecodesCorrectly()
    {
        // raw 5120 × 300 / 51200 = 30.0 V
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(consoleBatteryRaw: 5120);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.ConsoleBatteryVoltage.Should().BeApproximately(5120 * 300.0 / 51200.0, 0.001);
    }

    [TestMethod]
    public void ParseLoop1_ConsoleBattery_ZeroRaw_ReturnsZeroVolts()
    {
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(consoleBatteryRaw: 0);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.ConsoleBatteryVoltage.Should().Be(0.0);
    }

    [TestMethod]
    public void ParseLoop1_TransmitterBatteryStatus_AllOk_LowChannelsEmpty()
    {
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(txBatteryStatus: 0x0000);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.TransmitterBatteryStatus.Should().Be(0);
        packet.TransmitterLowBatteryChannels.Should().BeEmpty();
    }

    [TestMethod]
    public void ParseLoop1_TransmitterBatteryStatus_Channel1And3Low_Correct()
    {
        // Bit 0 = channel 1, bit 2 = channel 3
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(txBatteryStatus: 0x0005);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.TransmitterLowBatteryChannels.Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [TestMethod]
    public void ParseLoop1_ForecastRule_DecodesCorrectly()
    {
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(forecastRule: 7);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.ForecastRule.Should().Be(7);
        packet.ForecastString.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void ParseLoop1_ForecastIconNames_SunnyBit_ContainsSunny()
    {
        // 0x08 = bit 3 = Sunny
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(forecastIcons: 0x08);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.ForecastIconNames.Should().Contain("Sunny");
    }

    [TestMethod]
    public void ParseLoop1_ForecastIconNames_RainAndCloudy_ContainsBoth()
    {
        // 0x01 = Rain, 0x02 = Cloudy
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(forecastIcons: 0x03);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.ForecastIconNames.Should().Contain("Rain").And.Contain("Cloudy");
    }

    [TestMethod]
    public void ParseLoop1_SunriseTime_DecodesCorrectly()
    {
        // 638 = 06:38 → "06:38"
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(sunriseHhmm: 638);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.SunriseDisplay.Should().Be("06:38");
    }

    [TestMethod]
    public void ParseLoop1_SunsetTime_DecodesCorrectly()
    {
        // 2012 = 20:12 → "20:12"
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(sunsetHhmm: 2012);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.SunsetDisplay.Should().Be("20:12");
    }

    [TestMethod]
    public void ParseLoop1_MonthlyRainInches_DecodesCorrectly()
    {
        // 10 clicks × 0.01 in/click = 0.10 in
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(monthlyRainClicks: 10);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.MonthlyRainInches.Should().BeApproximately(0.10, 0.001);
    }

    [TestMethod]
    public void ParseLoop1_YearlyRainInches_DecodesCorrectly()
    {
        byte[] buf = PacketBuilder.BuildLoop1DataBytes(yearlyRainClicks: 250);
        var packet = Loop2Packet.Parse(buf, bucketType: 0);
        packet.YearlyRainInches.Should().BeApproximately(2.50, 0.001);
    }
}

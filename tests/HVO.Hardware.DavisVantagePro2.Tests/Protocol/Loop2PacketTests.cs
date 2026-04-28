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
        buf[4] = DavisProtocol.PacketTypeLoop1; // LOOP1, not LOOP2
        Action act = () => Loop2Packet.Parse(buf, bucketType: 0);
        act.Should().Throw<DavisUnknownPacketTypeException>();
    }
}

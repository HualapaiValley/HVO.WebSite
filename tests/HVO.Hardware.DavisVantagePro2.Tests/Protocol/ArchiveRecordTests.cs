using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;

namespace HVO.Hardware.DavisVantagePro2.Tests.Protocol;

[TestClass]
public class ArchiveRecordTests
{
    // ── Happy-path field decoding ─────────────────────────────────────────────

    [TestMethod]
    public void Parse_ValidRecord_ReturnsOutsideTemperature()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(outsideTempF: 65.5);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec.Should().NotBeNull();
        rec!.OutsideTemperatureF.Should().BeApproximately(65.5, 0.1);
    }

    [TestMethod]
    public void Parse_ValidRecord_ReturnsHighLowOutsideTemperature()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(highOutsideTempF: 70.0, lowOutsideTempF: 60.0);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.HighOutsideTemperatureF.Should().BeApproximately(70.0, 0.1);
        rec.LowOutsideTemperatureF.Should().BeApproximately(60.0, 0.1);
    }

    [TestMethod]
    public void Parse_ValidRecord_ReturnsInsideTemperature()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(insideTempF: 72.0);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.InsideTemperatureF.Should().BeApproximately(72.0, 0.1);
    }

    [TestMethod]
    public void Parse_ValidRecord_ReturnsHumidity()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(outsideHumidity: 60, insideHumidity: 40);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.OutsideHumidityPercent.Should().Be(60);
        rec.InsideHumidityPercent.Should().Be(40);
    }

    [TestMethod]
    public void Parse_ValidRecord_ReturnsBarometricPressure()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(barometricPressureInHg: 29.800);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.BarometricPressureInHg.Should().BeApproximately(29.800, 0.001);
    }

    [TestMethod]
    public void Parse_ValidRecord_ReturnsWindSpeedAndGust()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(windSpeedMph: 7, windGustMph: 14);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.WindSpeedMph.Should().Be(7);
        rec.WindGustMph.Should().Be(14);
    }

    [TestMethod]
    public void Parse_WindDirectionOctet8_Decodes180Degrees()
    {
        // Octet 8 × 22.5 = 180° (South)
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(windDirOctet: 8, windGustDirOctet: 8);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.WindDirectionDegrees.Should().BeApproximately(180.0, 0.01);
        rec.WindGustDirectionDegrees.Should().BeApproximately(180.0, 0.01);
    }

    [TestMethod]
    public void Parse_ValidRecord_ReturnsCorrectDateTime()
    {
        var expected = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Local);
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(dateTime: expected);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);

        rec!.DateTimeLocal.Year.Should().Be(2024);
        rec.DateTimeLocal.Month.Should().Be(6);
        rec.DateTimeLocal.Day.Should().Be(15);
        rec.DateTimeLocal.Hour.Should().Be(14);
        rec.DateTimeLocal.Minute.Should().Be(30);
    }

    [TestMethod]
    public void Parse_ArchiveIntervalMinutes_SetFromParameter()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes();
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 15);
        rec!.ArchiveIntervalMinutes.Should().Be(15);
    }

    [TestMethod]
    public void Parse_RainClicks_BucketType0_DecodesTo001InchUnits()
    {
        // 0.01-inch bucket: 5 clicks = 0.05 inches
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(rainClicks: 5);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.RainInches.Should().BeApproximately(0.05, 0.001);
    }

    // ── Null / dash sentinel handling ─────────────────────────────────────────

    [TestMethod]
    public void Parse_DashSentinelUvIndex_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes();
        buf[28] = 0xFF; // UV null sentinel (already set by builder)
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.UvIndex.Should().BeNull();
    }

    [TestMethod]
    public void Parse_DashSentinelSolarRadiation_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes();
        // 0x7FFF already set by the builder
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.SolarRadiationWm2.Should().BeNull();
    }

    // ── Unused record detection ───────────────────────────────────────────────

    [TestMethod]
    public void Parse_AllFF_ReturnsNull()
    {
        var buf = new byte[52];
        buf.AsSpan().Fill(0xFF);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec.Should().BeNull();
    }

    [TestMethod]
    public void Parse_AllZero_ReturnsNull()
    {
        var buf = new byte[52]; // all zeros
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec.Should().BeNull();
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_BufferTooShort_ThrowsDavisProtocolException()
    {
        byte[] shortBuf = new byte[30]; // need at least 52 bytes
        Action act = () => ArchiveRecord.Parse(shortBuf, bucketType: 0, archiveIntervalMinutes: 5);
        act.Should().Throw<DavisProtocolException>();
    }

    // ── Extra sensor fields ───────────────────────────────────────────────────

    [TestMethod]
    public void Parse_LeafTemp1_DecodesCorrectly()
    {
        // raw 108 → 108 - 90 = 18 °F
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(leafTemp1Raw: 108);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.LeafTemp1F.Should().Be(18.0);
    }

    [TestMethod]
    public void Parse_LeafTemp1_NullSentinel_ReturnsNull()
    {
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(leafTemp1Raw: 0xFF);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.LeafTemp1F.Should().BeNull();
    }

    [TestMethod]
    public void Parse_LeafTemp2_DecodesCorrectly()
    {
        // raw 100 → 100 - 90 = 10 °F
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(leafTemp2Raw: 100);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.LeafTemp2F.Should().Be(10.0);
    }

    [TestMethod]
    public void Parse_ExtraTemperatures_DecodesFirstValue()
    {
        // extra temp 1 raw = 130 → 130 - 90 = 40 °F
        var extras = new byte[] { 130, 0xFF, 0xFF };
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(extraTempsRaw: extras);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.ExtraTemperaturesF[0].Should().Be(40.0);
        rec.ExtraTemperaturesF[1].Should().BeNull();
    }

    [TestMethod]
    public void Parse_SoilMoisture_DecodesCorrectly()
    {
        // raw 30 = 30 centibars
        var soils = new byte[] { 30, 0xFF, 0xFF, 0xFF };
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(soilMoisturesRaw: soils);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.SoilMoisturesCb[0].Should().Be(30.0);
        rec.SoilMoisturesCb[1].Should().BeNull();
    }

    [TestMethod]
    public void Parse_LeafWetness_DecodesCorrectly()
    {
        // raw 7 = scale 7 (0–15)
        var wetness = new byte[] { 7, 0xFF };
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(leafWetnessRaw: wetness);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.LeafWetnessScaled[0].Should().Be(7.0);
        rec.LeafWetnessScaled[1].Should().BeNull();
    }

    [TestMethod]
    public void Parse_ExtraHumidity1_DecodesCorrectly()
    {
        var humids = new byte[] { 78, 0xFF };
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(extraHumiditiesRaw: humids);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.ExtraHumiditiesPercent[0].Should().Be(78.0);
        rec.ExtraHumiditiesPercent[1].Should().BeNull();
    }

    [TestMethod]
    public void Parse_ExtraHumidity1_NullSentinel_ReturnsNull()
    {
        var humids = new byte[] { 0xFF, 0xFF };
        byte[] buf = PacketBuilder.BuildArchiveDataBytes(extraHumiditiesRaw: humids);
        var rec = ArchiveRecord.Parse(buf, bucketType: 0, archiveIntervalMinutes: 5);
        rec!.ExtraHumiditiesPercent[0].Should().BeNull();
    }
}

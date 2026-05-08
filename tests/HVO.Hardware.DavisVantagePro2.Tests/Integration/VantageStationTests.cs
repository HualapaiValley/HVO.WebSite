using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Text;

namespace HVO.Hardware.DavisVantagePro2.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="VantageStation"/> using an in-process
/// <see cref="FakeDavisServer"/>.
///
/// Each test opens the TCP connection directly on the <see cref="DavisConsoleClient"/>
/// (bypassing <c>VantageStation.ConnectAsync</c> to avoid scripting the EEPROM reads),
/// then exercises the station method under test.
///
/// Protocol script reference for each station method:
/// <list type="bullet">
///   <item>GetCurrentConditionsAsync:  wake + LPS 1 1\n (8 b) → ACK + 99-byte LOOP2</item>
///   <item>GetConsoleTimeAsync:        wake + GETTIME\n  (8 b) → ACK + 8-byte time</item>
///   <item>GetReceptionStatsAsync:     wake + [inner wake] + RXCHECK\n (8 b) → text OK</item>
/// </list>
/// </summary>
[TestClass]
public class VantageStationTests
{
    private static (DavisConsoleClient client, VantageStation station) CreatePair(int port)
    {
        var client = new DavisConsoleClient(
            "127.0.0.1", port, TimeSpan.FromSeconds(8),
            NullLogger<DavisConsoleClient>.Instance);
        var station = new VantageStation(
            client, NullLogger<VantageStation>.Instance, maxTries: 1);
        return (client, station);
    }

    private static int EepromReadLength(ushort address, int bytes) =>
        Encoding.ASCII.GetByteCount($"{DavisProtocol.CmdEebrd} {address:X} {bytes:X}\n");

    private static int EepromWriteLength(ushort address, int bytes) =>
        Encoding.ASCII.GetByteCount($"{DavisProtocol.CmdEebwr} {address:X} {bytes:X}\n");

    // ── GetLoop1Async ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLoop1Async_ReturnsLoop1OnlyFields()
    {
        byte[] loop1 = PacketBuilder.BuildLoop1Packet(
            outsideTempF: 72.5,
            outsideHumidity: 55,
            forecastRule: 7,
            sunriseHhmm: 638,
            sunsetHhmm: 2012);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()                                    // wake before LPS
            .Step(8, [DavisProtocol.Ack, .. loop1])         // "LPS 1 1\n" (8 b) → ACK + LOOP1
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var result = await station.GetLoop1Async();

        result.OutsideTemperatureF.Should().BeApproximately(72.5, 0.1);
        result.OutsideHumidityPercent.Should().Be(55);
        result.ForecastRule.Should().Be(7);
        result.SunriseTime.Should().Be(638);
        result.SunsetTime.Should().Be(2012);
        result.ConsoleBatteryVoltage.Should().NotBeNull(); // LOOP1-only field
        // LOOP2-only derived fields must be absent in a LOOP1 packet
        result.DewPointF.Should().BeNull();
        result.HeatIndexF.Should().BeNull();
        result.WindChillF.Should().BeNull();

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public void ApplyStationSettings_HydratesCachedSetupValues()
    {
        var (_, station) = CreatePair(port: 0);
        var settings = new StationSettings
        {
            ArchiveIntervalSeconds = 600,
            LatitudeDegrees = 35.5,
            LongitudeDegrees = -113.8,
            AltitudeFeet = 2932,
            RainBucketType = 1,
            UseTimezoneCode = false,
            TimezoneCode = 0,
            GmtOffsetHours = -7
        };

        station.ApplyStationSettings(settings);

        station.ArchiveIntervalSeconds.Should().Be(600);
        station.RainBucketType.Should().Be(1);
        station.LatitudeDegrees.Should().Be(35.5);
        station.LongitudeDegrees.Should().Be(-113.8);
        station.AltitudeFeet.Should().Be(2932);
        station.ConsoleUtcOffset.Should().Be(TimeSpan.FromHours(-7));
        station.ConsoleTimeZoneLabel.Should().Be("GMT -7.00 h");
    }

    [TestMethod]
    public async Task SetLatitudeAsync_SendsNewSetupAfterEepromWrite()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(10, [DavisProtocol.Ack])
            .Step(4, [DavisProtocol.Ack])
            .Step(9, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetLatitudeAsync(35.5);

        server.ReceivedSteps.Should().HaveCount(4);
        station.LatitudeDegrees.Should().Be(35.5);
        Encoding.ASCII.GetString(server.ReceivedSteps[1]).Should().Be($"{DavisProtocol.CmdEebwr} {DavisProtocol.EepromLatitude:X} 2\n");
        Encoding.ASCII.GetString(server.ReceivedSteps[3]).Should().Be($"{DavisProtocol.CmdNewsetup}\n");

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetLongitudeAsync_SendsNewSetupAfterEepromWrite()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(10, [DavisProtocol.Ack])
            .Step(4, [DavisProtocol.Ack])
            .Step(9, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetLongitudeAsync(-113.8);

        server.ReceivedSteps.Should().HaveCount(4);
        station.LongitudeDegrees.Should().Be(-113.8);
        Encoding.ASCII.GetString(server.ReceivedSteps[1]).Should().Be($"{DavisProtocol.CmdEebwr} {DavisProtocol.EepromLongitude:X} 2\n");
        Encoding.ASCII.GetString(server.ReceivedSteps[3]).Should().Be($"{DavisProtocol.CmdNewsetup}\n");

        client.Dispose();
        await station.DisposeAsync();
    }

    // ── StreamLoop2Async ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task StreamLoop2Async_YieldsAllPacketsInOrder()
    {
        byte[] loop2a = PacketBuilder.BuildLoop2Packet(outsideTempF: 65.0);
        byte[] loop2b = PacketBuilder.BuildLoop2Packet(outsideTempF: 70.0);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()                                               // wake before LPS
            .Step(8, [DavisProtocol.Ack, .. loop2a, .. loop2b])        // "LPS 2 2\n" (8 b) → ACK + 2×LOOP2
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var packets = new List<Loop2Packet>();
        await foreach (var p in station.StreamLoop2Async(2))
            packets.Add(p);

        packets.Should().HaveCount(2);
        packets[0].OutsideTemperatureF.Should().BeApproximately(65.0, 0.1);
        packets[1].OutsideTemperatureF.Should().BeApproximately(70.0, 0.1);

        client.Dispose();
        await station.DisposeAsync();
    }

    // ── GetCurrentConditionsAsync ─────────────────────────────────────────────

    [TestMethod]
    public async Task GetCurrentConditionsAsync_ReturnsDecodedLoop2Fields()
    {
        byte[] loop1 = PacketBuilder.BuildLoop1Packet();
        byte[] loop2 = PacketBuilder.BuildLoop2Packet(
            outsideTempF: 65.3,
            insideTempF: 71.0,
            outsideHumidity: 58,
            baroPressureInHg: 29.850,
            windSpeedMph: 5,
            windDirDeg: 180,
            dewPointF: 50.0);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()                                            // wake before LPS
            .Step(8, [DavisProtocol.Ack, .. loop1, .. loop2])  // "LPS 3 2\n" (8 b) → ACK + LOOP1 + LOOP2
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var result = await station.GetCurrentConditionsAsync();

        result.Should().NotBeNull();
        result.OutsideTemperatureF.Should().BeApproximately(65.3, 0.1);
        result.InsideTemperatureF.Should().BeApproximately(71.0, 0.1);
        result.OutsideHumidityPercent.Should().Be(58);
        result.BarometricPressureInHg.Should().BeApproximately(29.850, 0.001);
        result.WindSpeedMph.Should().Be(5.0);
        result.WindDirectionDegrees.Should().Be(180.0);
        result.DewPointF.Should().Be(50.0);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetCurrentConditionsAsync_SolarAndUvAbsent_ReturnsNull()
    {
        // The builder initialises solar and UV to their null sentinels by default
        byte[] loop1 = PacketBuilder.BuildLoop1Packet();
        byte[] loop2 = PacketBuilder.BuildLoop2Packet();

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Ack, .. loop1, .. loop2])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var result = await station.GetCurrentConditionsAsync();

        result.SolarRadiationWm2.Should().BeNull();
        result.UvIndex.Should().BeNull();

        client.Dispose();
        await station.DisposeAsync();
    }

    // ── GetConsoleTimeAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task GetConsoleTimeAsync_ReturnsCorrectDateTime()
    {
        var consoleTime = new DateTime(2025, 3, 20, 14, 30, 45, DateTimeKind.Local);
        byte[] timeResp = PacketBuilder.BuildGetTimeResponse(consoleTime);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()                                          // wake before GETTIME
            .Step(8, [DavisProtocol.Ack, .. timeResp])           // "GETTIME\n" (8 b) → ACK + time
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        DateTime result = await station.GetConsoleTimeAsync();

        result.Year.Should().Be(2025);
        result.Month.Should().Be(3);
        result.Day.Should().Be(20);
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
        result.Second.Should().Be(45);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetStationInfoAsync_ParsesHardwareFirmwareAndConsoleTime()
    {
        var consoleTime = new DateTime(2025, 3, 20, 14, 30, 45, DateTimeKind.Local);
        byte[] timeResp = PacketBuilder.BuildGetTimeResponse(consoleTime);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(6, [DavisProtocol.Ack, DavisProtocol.HardwareVantageVue])
            .WakeStep()
            .Step(5, PacketBuilder.BuildTextResponse("3.15"))
            .WakeStep()
            .Step(4, PacketBuilder.BuildTextResponse("Mar 29 2013"))
            .Step(8, [DavisProtocol.Ack, .. timeResp])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var info = await station.GetStationInfoAsync();

        info.HardwareType.Should().Be(DavisProtocol.HardwareVantageVue);
        info.HardwareName.Should().Be("Vantage Vue");
        info.ModelType.Should().Be(2);
        info.FirmwareVersion.Should().Be("3.15");
        info.FirmwareDate.Should().Be("Mar 29 2013");
        info.ConsoleTime.Should().Be(consoleTime);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetStationInfoAsync_VantageProHardware_ReturnsProModel()
    {
        var consoleTime = new DateTime(2025, 3, 20, 14, 30, 45, DateTimeKind.Local);
        byte[] timeResp = PacketBuilder.BuildGetTimeResponse(consoleTime);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(6, [DavisProtocol.Ack, DavisProtocol.HardwareVantagePro])
            .WakeStep()
            .Step(5, PacketBuilder.BuildTextResponse("1.90"))
            .WakeStep()
            .Step(4, PacketBuilder.BuildTextResponse("Jan 01 2008"))
            .Step(8, [DavisProtocol.Ack, .. timeResp])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var info = await station.GetStationInfoAsync();

        info.HardwareType.Should().Be(DavisProtocol.HardwareVantagePro);
        info.HardwareName.Should().Be("Vantage Pro");
        info.ModelType.Should().Be(1);
        info.FirmwareVersion.Should().Be("1.90");
        info.FirmwareDate.Should().Be("Jan 01 2008");
        info.ConsoleTime.Should().Be(consoleTime);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetStationSettingsAsync_DecodesEepromBackedSettings()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromReadLength(DavisProtocol.EepromUnitBits, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0xE6))
            .Step(EepromReadLength(DavisProtocol.EepromSetupBits, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x20))
            .Step(EepromReadLength(DavisProtocol.EepromRainYearStart, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x07))
            .Step(EepromReadLength(DavisProtocol.EepromArchiveInterval, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x0F))
            .Step(EepromReadLength(DavisProtocol.EepromGmtOrZone, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x01))
            .Step(EepromReadLength(DavisProtocol.EepromManOrAuto, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x01))
            .Step(EepromReadLength(DavisProtocol.EepromDaylightSavings, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x01))
            .Step(EepromReadLength(DavisProtocol.EepromTimezoneCode, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x05))
            .Step(EepromReadLength(DavisProtocol.EepromTempLogging, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x01))
            .Step(EepromReadLength(DavisProtocol.EepromLatitude, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x63, 0x01))
            .Step(EepromReadLength(DavisProtocol.EepromLongitude, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x8E, 0xFB))
            .Step(EepromReadLength(DavisProtocol.EepromAltitude, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x74, 0x0B))
            .Step(EepromReadLength(DavisProtocol.EepromGmtOffset, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x44, 0xFD))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var settings = await station.GetStationSettingsAsync();

        settings.ArchiveIntervalMinutes.Should().Be(15);
        settings.RainBucketType.Should().Be(2);
        settings.RainYearStartMonth.Should().Be(7);
        settings.DstSetting.Should().Be("ON");
        settings.UseTimezoneCode.Should().BeFalse();
        settings.GmtOffsetHours.Should().BeApproximately(-7.0, 0.01);
        settings.TemperatureLogging.Should().Be("LAST");
        settings.BarometerUnits.Should().Be("hPa");
        settings.TemperatureUnits.Should().Be("°F×10");
        settings.RainUnits.Should().Be("mm");
        settings.WindUnits.Should().Be("knots");
        settings.LatitudeDegrees.Should().BeApproximately(35.5, 0.01);
        settings.LongitudeDegrees.Should().BeApproximately(-113.8, 0.01);
        settings.AltitudeFeet.Should().Be(2932);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetTransmittersAsync_DecodesRetransmitAndSensorIdsPerProtocol()
    {
        byte[] txBytes = new byte[16];
        txBytes[2] = 0x03;
        txBytes[3] = 0x31;

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromReadLength(DavisProtocol.EepromUseTx, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0b0000_0010))
            .Step(EepromReadLength(DavisProtocol.EepromRetransmit, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x02))
            .Step(EepromReadLength(DavisProtocol.EepromTransmitters, 16), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(txBytes))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var tx = await station.GetTransmittersAsync();
        var channel2 = tx.Single(t => t.Channel == 2);

        channel2.TransmitterType.Should().Be("temp_hum");
        channel2.IsActive.Should().BeTrue();
        channel2.IsRetransmitting.Should().BeTrue();
        channel2.ExtraTemperatureId.Should().Be(2);
        channel2.ExtraHumidityId.Should().Be(3);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetCalibrationAsync_DecodesSignedOffsets()
    {
        byte[] tempBlock = new byte[27];
        tempBlock[0] = unchecked((byte)(sbyte)-5);
        tempBlock[2] = 8;
        tempBlock[3] = unchecked((byte)(sbyte)-3);
        tempBlock[10] = 12;
        tempBlock[14] = unchecked((byte)(sbyte)-7);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromReadLength(DavisProtocol.EepromTempCalib, 27), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(tempBlock))
            .Step(EepromReadLength(DavisProtocol.EepromInHumidCalib, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(unchecked((byte)(sbyte)-4)))
            .Step(EepromReadLength(DavisProtocol.EepromOutHumidCalib, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse((byte)6))
            .Step(EepromReadLength(DavisProtocol.EepromWindDirCalib, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x2D, 0x00))
            .Step(EepromReadLength((ushort)(DavisProtocol.EepromOutHumidCalib + 1), 7), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(1, 2, 3, 4, 5, 6, 7))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var calibration = await station.GetCalibrationAsync();

        calibration.InsideTempOffsetF.Should().BeApproximately(-0.5, 0.01);
        calibration.OutsideTempOffsetF.Should().BeApproximately(0.8, 0.01);
        calibration.ExtraTempOffsets[0].Should().BeApproximately(-0.3, 0.01);
        calibration.SoilTempOffsets[0].Should().BeApproximately(1.2, 0.01);
        calibration.LeafTempOffsets[0].Should().BeApproximately(-0.7, 0.01);
        calibration.InsideHumidOffsetPct.Should().Be(-4);
        calibration.OutsideHumidOffsetPct.Should().Be(6);
        calibration.WindDirOffsetDegrees.Should().Be(45);
        calibration.ExtraHumidOffsets[6].Should().Be(7);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetHeardTransmitterIdsAsync_ParsesReceiversBitmap()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(10, PacketBuilder.BuildReceiversResponse(0b0010_0101))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var heard = await station.GetHeardTransmitterIdsAsync();

        heard.Should().Equal([1, 3, 6]);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetAlarmThresholdsAsync_DecodesAlarmBlock()
    {
        byte[] block = Enumerable.Repeat((byte)0xFF, DavisProtocol.EepromAlarmBlockSize).ToArray();
        block[0] = 5;
        block[1] = 8;
        BitConverter.GetBytes((ushort)730).CopyTo(block, 2);
        BitConverter.GetBytes(unchecked((ushort)~730)).CopyTo(block, 4);
        block[6] = 58;
        block[40] = 30;
        block[42] = 40;
        block[43] = 50;
        block[58] = 88;
        block[63] = 32;
        block[65] = 42;
        block[66] = 15;
        block[67] = 10;
        block[83] = 0xB0;
        block[84] = 0x04;
        block[85] = 0x0A;
        block[86] = 0x00;
        block[93] = 133;

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromReadLength(DavisProtocol.EepromAlarmStart, DavisProtocol.EepromAlarmBlockSize), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(block))
            .Start();

        var (client, station) = CreatePair(server.Port);
        station.ApplyStationSettings(new StationSettings { RainBucketType = 0 });
        await client.OpenAsync(CancellationToken.None);

        var alarms = await station.GetAlarmThresholdsAsync();

        alarms.RisingBarTrendInHg.Should().BeApproximately(0.005, 0.0001);
        alarms.FallingBarTrendInHg.Should().BeApproximately(0.008, 0.0001);
        alarms.TimeAlarm.Should().Be(new TimeOnly(7, 30));
        alarms.LowInsideTemperatureF.Should().Be(-32);
        alarms.LowInsideHumidityPercent.Should().Be(30);
        alarms.LowOutsideHumidityPercent.Should().Be(40);
        alarms.LowExtraHumidityPercent[0].Should().Be(50);
        alarms.LowDewPointF.Should().Be(-32);
        alarms.WindSpeedMph.Should().Be(32);
        alarms.UvIndex.Should().BeApproximately(4.2, 0.01);
        alarms.UvDoseMeds.Should().BeApproximately(1.5, 0.01);
        alarms.LowSoilMoistureCb[0].Should().Be(10);
        alarms.SolarRadiationWm2.Should().Be(1200);
        alarms.RainRateInchesPerHour.Should().BeApproximately(0.10, 0.001);
        alarms.DailyEtInches.Should().BeApproximately(0.133, 0.001);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetAlarmThresholdsAsync_WritesEncodedAlarmBlock()
    {
        var thresholds = new AlarmThresholds
        {
            RisingBarTrendInHg = 0.010,
            FallingBarTrendInHg = 0.012,
            TimeAlarm = new TimeOnly(6, 45),
            LowInsideTemperatureF = -10,
            HighOutsideTemperatureF = 110,
            LowOutsideHumidityPercent = 25,
            WindSpeedMph = 30,
            UvIndex = 5.2,
            SolarRadiationWm2 = 1000,
            RainRateInchesPerHour = 0.15,
            DailyEtInches = 0.125,
            LowExtraTemperaturesF = [1, null, null, null, null, null, null],
            HighExtraHumidityPercent = [null, 65, null, null, null, null, null]
        };

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromAlarmStart, DavisProtocol.EepromAlarmBlockSize), [DavisProtocol.Ack])
            .Step(DavisProtocol.EepromAlarmBlockSize + 2, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        station.ApplyStationSettings(new StationSettings { RainBucketType = 0 });
        await client.OpenAsync(CancellationToken.None);

        await station.SetAlarmThresholdsAsync(thresholds);

        server.ReceivedSteps.Should().HaveCount(3);
        byte[] payload = server.ReceivedSteps[2];
        payload.Should().HaveCount(DavisProtocol.EepromAlarmBlockSize + 2);
        payload[0].Should().Be(10);
        payload[1].Should().Be(12);
        payload[6].Should().Be(80);
        payload[9].Should().Be(200);
        payload[42].Should().Be(25);
        payload[52].Should().Be(65);
        payload[63].Should().Be(30);
        payload[65].Should().Be(52);
        payload[93].Should().Be(125);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetAlarmThresholdsAsync_NullLists_AreTreatedAsEmpty()
    {
        var thresholds = new AlarmThresholds
        {
            LowExtraTemperaturesF = null!,
            HighExtraTemperaturesF = null!,
            LowExtraHumidityPercent = null!,
            HighExtraHumidityPercent = null!
        };

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromAlarmStart, DavisProtocol.EepromAlarmBlockSize), [DavisProtocol.Ack])
            .Step(DavisProtocol.EepromAlarmBlockSize + 2, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        station.ApplyStationSettings(new StationSettings { RainBucketType = 0 });
        await client.OpenAsync(CancellationToken.None);

        Func<Task> act = () => station.SetAlarmThresholdsAsync(thresholds);
        await act.Should().NotThrowAsync();

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetAlarmThresholdsAsync_OutOfRangeValue_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetAlarmThresholdsAsync(new AlarmThresholds { UvIndex = 30.0 });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*UvIndex must be between 0*25.4*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetBarometerDataAsync_ParsesUsingInvariantCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            await using var server = new FakeDavisServer();
            server
                .WakeStep()
                .WakeStep()
                .Step(8, PacketBuilder.BuildTextResponse(
                    "BAR  29.990",
                    "ELEVATION  4500",
                    "DEW POINT  55",
                    "VIRTUAL TEMP  62",
                    "C  2.3",
                    "R  1.003",
                    "BARCAL  0.012",
                    "GAIN  1",
                    "OFFSET  0"))
                .Start();

            var (client, station) = CreatePair(server.Port);
            await client.OpenAsync(CancellationToken.None);

            var bar = await station.GetBarometerDataAsync();
            bar.CurrentPressureInHg.Should().BeApproximately(29.990, 0.001);

            client.Dispose();
            await station.DisposeAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestMethod]
    public async Task ClearAlarmThresholdsAsync_WaitsForDone()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, PacketBuilder.BuildTextResponse("DONE"))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        Func<Task> act = () => station.ClearAlarmThresholdsAsync();
        await act.Should().NotThrowAsync();

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTransmitterAsync_EncodesTempAndHumidityIdsPerProtocol()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromReadLength(DavisProtocol.EepromUseTx, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x00))
            .Step(EepromWriteLength((ushort)(DavisProtocol.EepromTransmitters + 2), 2), [DavisProtocol.Ack])
            .Step(4, [DavisProtocol.Ack])
            .Step(EepromWriteLength(DavisProtocol.EepromUseTx, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Step(9, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetTransmitterAsync(2, TransmitterType.TempHumidity, 2, 3, null);

        server.ReceivedSteps[4][0].Should().Be(0x03);
        server.ReceivedSteps[4][1].Should().Be(0x31);
        server.ReceivedSteps[6][0].Should().Be(0b0000_0010);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetRetransmitAsync_WritesDirectChannelNumber()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromRetransmit, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Step(9, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetRetransmitAsync(3);

        server.ReceivedSteps[2][0].Should().Be(3);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetArchiveIntervalAsync_SendsSetperCommand()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .WakeStep()
            .Step(10, PacketBuilder.BuildTextResponse())
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetArchiveIntervalAsync(15);

        station.ArchiveIntervalSeconds.Should().Be(900);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetLampAsync_SendsLampCommand()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .WakeStep()
            .Step(8, PacketBuilder.BuildTextResponse())
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetLampAsync(true);

        Encoding.ASCII.GetString(server.ReceivedSteps[2]).Should().Be("LAMPS 1\n");

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetBarometerDataAsync_ParsesTextResponse()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .WakeStep()
            .Step(8, PacketBuilder.BuildTextResponse(
                "BAR  29.990",
                "ELEVATION  4500",
                "DEW POINT  55",
                "VIRTUAL TEMP  62",
                "C  2.3",
                "R  1.003",
                "BARCAL  0.012",
                "GAIN  1",
                "OFFSET  0"))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var bar = await station.GetBarometerDataAsync();

        bar.CurrentPressureInHg.Should().BeApproximately(29.990, 0.001);
        bar.AltitudeFeet.Should().Be(4500);
        bar.DewPointF.Should().Be(55);
        bar.VirtualTemperatureF.Should().Be(62);
        bar.CorrectionFactor.Should().BeApproximately(2.3, 0.01);
        bar.CorrectionRatio.Should().BeApproximately(1.003, 0.001);
        bar.CorrectionConstantInHg.Should().BeApproximately(0.012, 0.001);
        bar.Gain.Should().Be(1);
        bar.ErrorOffset.Should().Be(0);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetBarometerDataAsync_TruncatedResponse_ThrowsDavisProtocolException()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .WakeStep()
            .Step(8, PacketBuilder.BuildTextResponse("BAR  29.990"))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        Func<Task> act = () => station.GetBarometerDataAsync();
        await act.Should().ThrowAsync<DavisProtocolException>();

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetBarometerAsync_SendsBarCommandAndRefreshesSetup()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .WakeStep()
            .Step(13, PacketBuilder.BuildTextResponse())
            .Step(EepromReadLength(DavisProtocol.EepromSetupBits, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x00))
            .Step(EepromReadLength(DavisProtocol.EepromArchiveInterval, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x0F))
            .Step(EepromReadLength(DavisProtocol.EepromGmtOrZone, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x00))
            .Step(EepromReadLength(DavisProtocol.EepromTimezoneCode, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x05))
            .Step(EepromReadLength(DavisProtocol.EepromGmtOffset, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x44, 0xFD))
            .Step(EepromReadLength(DavisProtocol.EepromLatitude, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x63, 0x01))
            .Step(EepromReadLength(DavisProtocol.EepromLongitude, 2), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x8E, 0xFB))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetBarometerAsync(29.991, -75);

        Encoding.ASCII.GetString(server.ReceivedSteps[2]).Should().StartWith("BAR=29991 -75");

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetConsoleTimeAsync_SendsSettimeCommandAndPayload()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Ack])
            .Step(8, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetConsoleTimeAsync();

        Encoding.ASCII.GetString(server.ReceivedSteps[1]).Should().Be("SETTIME\n");
        server.ReceivedSteps[2].Should().HaveCount(8);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetAltitudeAsync_WritesAltitudeEepromAndCachesEncodedValue()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromAltitude, 2), [DavisProtocol.Ack])
            .Step(4, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetAltitudeAsync(2932.6);

        station.AltitudeFeet.Should().Be(2932);
        server.ReceivedSteps[2][0].Should().Be(0x74);
        server.ReceivedSteps[2][1].Should().Be(0x0B);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetRainBucketTypeAsync_WritesSetupBitsAndRunsNewSetup()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromReadLength(DavisProtocol.EepromSetupBits, 1), [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildCrcResponse(0x00))
            .Step(EepromWriteLength(DavisProtocol.EepromSetupBits, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Step(9, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetRainBucketTypeAsync(2);

        station.RainBucketType.Should().Be(2);
        server.ReceivedSteps[4][0].Should().Be(0x20);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetRainYearStartAsync_WritesMonthByte()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromRainYearStart, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetRainYearStartAsync(7);

        server.ReceivedSteps[2][0].Should().Be(7);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetDstAsync_WritesManualAndDstBits()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromManOrAuto, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Step(EepromWriteLength(DavisProtocol.EepromDaylightSavings, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetDstAsync(DstMode.On);

        server.ReceivedSteps[2][0].Should().Be(1);
        server.ReceivedSteps[4][0].Should().Be(1);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTimezoneCodeAsync_WritesZoneModeAndCode()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromGmtOrZone, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Step(EepromWriteLength(DavisProtocol.EepromTimezoneCode, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetTimezoneCodeAsync(5);

        station.UseTimezoneCode.Should().BeTrue();
        station.TimezoneCode.Should().Be(5);
        server.ReceivedSteps[2][0].Should().Be(0);
        server.ReceivedSteps[4][0].Should().Be(5);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTimezoneOffsetAsync_WritesOffsetModeAndValue()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromGmtOrZone, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Step(EepromWriteLength(DavisProtocol.EepromGmtOffset, 2), [DavisProtocol.Ack])
            .Step(4, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetTimezoneOffsetAsync(-700);

        station.UseTimezoneCode.Should().BeFalse();
        station.GmtOffsetHours.Should().BeApproximately(-7.0, 0.01);
        server.ReceivedSteps[2][0].Should().Be(1);
        server.ReceivedSteps[4][0].Should().Be(0x44);
        server.ReceivedSteps[4][1].Should().Be(0xFD);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTemperatureLoggingAsync_WritesLoggingModeByte()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromTempLogging, 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetTemperatureLoggingAsync(TempLogging.Last);

        server.ReceivedSteps[2][0].Should().Be(1);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationWindDirAsync_WritesSignedShort()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromWindDirCalib, 2), [DavisProtocol.Ack])
            .Step(4, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetCalibrationWindDirAsync(-45);

        server.ReceivedSteps[2][0].Should().Be(0xD3);
        server.ReceivedSteps[2][1].Should().Be(0xFF);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationTempAsync_InTempWritesValueAndComplement()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength(DavisProtocol.EepromTempCalib, 2), [DavisProtocol.Ack])
            .Step(4, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetCalibrationTempAsync("inTemp", 1.2);

        server.ReceivedSteps[2][0].Should().Be(12);
        server.ReceivedSteps[2][1].Should().Be(unchecked((byte)~12));

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationHumidityAsync_ExtraChannelWritesSignedByte()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(EepromWriteLength((ushort)(DavisProtocol.EepromOutHumidCalib + 2), 1), [DavisProtocol.Ack])
            .Step(3, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.SetCalibrationHumidityAsync("extraHumid2", -10);

        server.ReceivedSteps[2][0].Should().Be(unchecked((byte)(sbyte)-10));

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetArchiveIntervalAsync_InvalidMinutes_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetArchiveIntervalAsync(7);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid archive interval*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetRainBucketTypeAsync_InvalidBucketCode_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetRainBucketTypeAsync(3);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Bucket code must be 0, 1, or 2*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetRainYearStartAsync_InvalidMonth_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetRainYearStartAsync(13);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Month must be 1*12*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTimezoneCodeAsync_InvalidCode_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetTimezoneCodeAsync(32);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Timezone code must be 0*31*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationWindDirAsync_OutOfRange_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetCalibrationWindDirAsync(360);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Wind dir offset must be*359*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationTempAsync_InvalidVariable_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetCalibrationTempAsync("badTemp", 1.2);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown temperature variable*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationTempAsync_OutOfRangeOffset_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetCalibrationTempAsync("inTemp", 13.0);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Temp offset must be*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationHumidityAsync_InvalidVariable_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetCalibrationHumidityAsync("badHumid", 5);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown humidity variable*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetCalibrationHumidityAsync_OutOfRangeOffset_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetCalibrationHumidityAsync("inHumid", 101);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Humidity offset must be*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTransmitterAsync_InvalidChannel_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetTransmitterAsync(0, TransmitterType.Iss, null, null, null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Channel must be 1*8*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTransmitterAsync_InvalidExtraTempId_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetTransmitterAsync(1, TransmitterType.TempHumidity, 8, 1, null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Extra temperature sensor ID must be 1*7*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetTransmitterAsync_InvalidExtraHumidityId_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetTransmitterAsync(1, TransmitterType.TempHumidity, 1, 8, null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Extra humidity sensor ID must be 1*7*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task SetRetransmitAsync_InvalidChannel_ThrowsArgumentException()
    {
        var (_, station) = CreatePair(port: 1);

        Func<Task> act = () => station.SetRetransmitAsync(9);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Channel must be 0*8*");
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task ClearArchiveAsync_SendsClrlogCommand()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.ClearArchiveAsync();

        Encoding.ASCII.GetString(server.ReceivedSteps[1]).Should().Be("CLRLOG\n");

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task ClearActiveAlarmBitsAsync_SendsClrbitsCommand()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Ack])
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        await station.ClearActiveAlarmBitsAsync();

        Encoding.ASCII.GetString(server.ReceivedSteps[1]).Should().Be("CLRBITS\n");

        client.Dispose();
        await station.DisposeAsync();
    }

    // ── GetReceptionStatsAsync ────────────────────────────────────────────────

    [TestMethod]
    public async Task GetReceptionStatsAsync_ParsesRxcheckResponse()
    {
        // GetReceptionStatsAsync:
        //   1. VantageStation calls WakeAsync                     → WakeStep #1
        //   2. SendCommandAsync("RXCHECK\n") calls WakeAsync      → WakeStep #2
        //   3. SendCommandAsync sends "RXCHECK\n" (8 bytes)       → Step(8, text)
        byte[] rxResponse = PacketBuilder.BuildTextResponse("12345 67 2 12340 3");

        await using var server = new FakeDavisServer();
        server
            .WakeStep()                 // wake #1 (explicit in station method)
            .WakeStep()                 // wake #2 (inside SendCommandAsync)
            .Step(8, rxResponse)        // "RXCHECK\n" (8 bytes)
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var result = await station.GetReceptionStatsAsync();

        result.TotalPacketsReceived.Should().Be(12345);
        result.TotalPacketsMissed.Should().Be(67);
        result.NumberOfResynchronizations.Should().Be(2);
        result.LongestGoodStretch.Should().Be(12340);
        result.NumberOfCrcErrors.Should().Be(3);
        result.ReceptionPercent.Should().BeApproximately(99.46, 0.01);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetReceptionStatsAsync_TruncatedResponse_ThrowsDavisProtocolException()
    {
        byte[] rxResponse = PacketBuilder.BuildTextResponse("12345 67 2");

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .WakeStep()
            .Step(8, rxResponse)
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        Func<Task> act = () => station.GetReceptionStatsAsync();
        await act.Should().ThrowAsync<DavisProtocolException>();

        client.Dispose();
        await station.DisposeAsync();
    }

    // ── Concurrency — semaphore serialization ─────────────────────────────────

    [TestMethod]
    public async Task GetCurrentConditionsAsync_TwoConcurrentCalls_BothSucceed()
    {
        // Two GetCurrentConditionsAsync tasks race to acquire the internal
        // SemaphoreSlim(1,1).  Script two full wake+LPS interactions.
        byte[] loop1a = PacketBuilder.BuildLoop1Packet();
        byte[] loop2a = PacketBuilder.BuildLoop2Packet(outsideTempF: 65.0);
        byte[] loop1b = PacketBuilder.BuildLoop1Packet();
        byte[] loop2b = PacketBuilder.BuildLoop2Packet(outsideTempF: 70.0);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Ack, .. loop1a, .. loop2a]) // 1st call
            .WakeStep()
            .Step(8, [DavisProtocol.Ack, .. loop1b, .. loop2b]) // 2nd call
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        // Fire both calls concurrently — the semaphore ensures serial execution
        var t1 = station.GetCurrentConditionsAsync();
        var t2 = station.GetCurrentConditionsAsync();
        var results = await Task.WhenAll(t1, t2);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.OutsideTemperatureF.Should().NotBeNull());

        var temps = results
            .Select(r => r.OutsideTemperatureF!.Value)
            .OrderBy(t => t)
            .ToList();

        temps[0].Should().BeApproximately(65.0, 0.1);
        temps[1].Should().BeApproximately(70.0, 0.1);

        client.Dispose();
        await station.DisposeAsync();
    }

    // ── GetArchiveSinceAsync ──────────────────────────────────────────────────
    //
    // DMPAFT exchange (per GetArchiveSinceAsync implementation):
    //   WakeAsync                         → WakeStep
    //   SendDataAsync("DMPAFT\n", 7 b)    → Step(7, [ACK])
    //   SendDataWithCrc16Async(4+2=6 b)   → Step(6, [ACK])
    //   GetDataWithCrc16Async(6)          → Step(0, DmpaftHeader)
    //   per page: GetDataWithCrc16Async(267, prompt:[ACK])
    //                                     → Step(1, ArchivePage)

    [TestMethod]
    public async Task GetArchiveSinceAsync_SinglePage_YieldsDecodedRecord()
    {
        var recordTime = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Local);
        byte[] rec0 = PacketBuilder.BuildArchiveDataBytes(
            dateTime: recordTime,
            outsideTempF: 68.5,
            highOutsideTempF: 72.0,
            lowOutsideTempF: 65.0,
            insideTempF: 74.0,
            outsideHumidity: 55,
            insideHumidity: 42,
            barometricPressureInHg: 29.850,
            windSpeedMph: 6,
            windGustMph: 12);

        byte[] page = PacketBuilder.BuildArchivePage(0, rec0);  // remaining 4 slots = zeros (null)

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])               // "DMPAFT\n"
            .Step(6, [DavisProtocol.Ack])               // date stamp + CRC
            .Step(0, PacketBuilder.BuildDmpaftHeader(1)) // nPages=1, startIndex=0
            .Step(1, page)                               // ACK prompt → 267-byte page
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var records = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(DateTime.MinValue))
            records.Add(r);

        records.Should().HaveCount(1);
        records[0].DateTimeLocal.Should().Be(recordTime);
        records[0].OutsideTemperatureF.Should().BeApproximately(68.5, 0.1);
        records[0].HighOutsideTemperatureF.Should().BeApproximately(72.0, 0.1);
        records[0].LowOutsideTemperatureF.Should().BeApproximately(65.0, 0.1);
        records[0].InsideTemperatureF.Should().BeApproximately(74.0, 0.1);
        records[0].OutsideHumidityPercent.Should().Be(55);
        records[0].InsideHumidityPercent.Should().Be(42);
        records[0].BarometricPressureInHg.Should().BeApproximately(29.850, 0.001);
        records[0].WindSpeedMph.Should().Be(6);
        records[0].WindGustMph.Should().Be(12);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetArchiveSinceAsync_ZeroPages_YieldsNoRecords()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])               // "DMPAFT\n"
            .Step(6, [DavisProtocol.Ack])               // date stamp + CRC
            .Step(0, PacketBuilder.BuildDmpaftHeader(0)) // nPages=0
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var records = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(DateTime.MinValue))
            records.Add(r);

        records.Should().BeEmpty();

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetArchiveSinceAsync_NullRecordInPage_TerminatesEnumerationEarly()
    {
        var baseTime = new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Local);
        byte[] rec0 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime, outsideTempF: 65.0);
        byte[] rec1 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime.AddMinutes(5), outsideTempF: 66.0);
        // rec2 is intentionally omitted → zeros (null sentinel → yield break)

        byte[] page = PacketBuilder.BuildArchivePage(0, rec0, rec1);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])
            .Step(6, [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildDmpaftHeader(1))
            .Step(1, page)
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var records = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(DateTime.MinValue))
            records.Add(r);

        records.Should().HaveCount(2);
        records[0].OutsideTemperatureF.Should().BeApproximately(65.0, 0.1);
        records[1].OutsideTemperatureF.Should().BeApproximately(66.0, 0.1);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetArchiveSinceAsync_MaxRecordsLessThanPage_StopsAfterLimit()
    {
        // Page has 3 valid records; maxRecords=2 should yield break after the second.
        var baseTime = new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Local);
        byte[] rec0 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime, outsideTempF: 61.0);
        byte[] rec1 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime.AddMinutes(30), outsideTempF: 62.0);
        byte[] rec2 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime.AddMinutes(60), outsideTempF: 63.0);
        byte[] page = PacketBuilder.BuildArchivePage(0, rec0, rec1, rec2);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])
            .Step(6, [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildDmpaftHeader(1))
            .Step(1, page)
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var records = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(DateTime.MinValue, maxRecords: 2))
            records.Add(r);

        records.Should().HaveCount(2);
        records[0].OutsideTemperatureF.Should().BeApproximately(61.0, 0.1);
        records[1].OutsideTemperatureF.Should().BeApproximately(62.0, 0.1);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetArchiveSinceAsync_MaxRecords_SemaphoreReleasedAfterEarlyExit()
    {
        // After a maxRecords early exit the semaphore must be released so a
        // subsequent station call can acquire it without deadlocking.
        var baseTime = new DateTime(2025, 6, 15, 14, 0, 0, DateTimeKind.Local);
        byte[] rec0 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime, outsideTempF: 64.0);
        byte[] rec1 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime.AddMinutes(30), outsideTempF: 65.0);
        byte[] page = PacketBuilder.BuildArchivePage(0, rec0, rec1);

        await using var server = new FakeDavisServer();
        server
            // First call: GetArchiveSinceAsync(maxRecords=1) — early exit after rec0
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])               // "DMPAFT\n"
            .Step(6, [DavisProtocol.Ack])               // date stamp + CRC
            .Step(0, PacketBuilder.BuildDmpaftHeader(1)) // nPages=1
            .Step(1, page)                               // ACK prompt → 267-byte page
                                                         // Second call: GetArchiveSinceAsync — nPages=0, proves lock was released
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])
            .Step(6, [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildDmpaftHeader(0)) // nPages=0 → yields nothing
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var firstBatch = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(DateTime.MinValue, maxRecords: 1))
            firstBatch.Add(r);

        firstBatch.Should().HaveCount(1);
        firstBatch[0].OutsideTemperatureF.Should().BeApproximately(64.0, 0.1);

        // If the lock was not released this await hangs — MSTest will time it out
        var secondBatch = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(DateTime.MinValue))
            secondBatch.Add(r);

        secondBatch.Should().BeEmpty();

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetArchiveSinceAsync_ZeroPagesNonMinValue_WithFallback_RetryYieldsRecords()
    {
        // First DMPAFT (specific date) returns 0 pages — firmware quirk for old dates.
        // Second DMPAFT (all-zeros fallback) returns 1 page with a valid record.
        var recordTime = new DateTime(2026, 4, 19, 8, 0, 0, DateTimeKind.Local);
        byte[] rec0 = PacketBuilder.BuildArchiveDataBytes(dateTime: recordTime, outsideTempF: 72.0);
        byte[] page = PacketBuilder.BuildArchivePage(0, rec0);

        var since = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local); // older than the record

        await using var server = new FakeDavisServer();
        server
            // First DMPAFT: returns 0 pages (firmware quirk)
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])               // "DMPAFT\n"
            .Step(6, [DavisProtocol.Ack])               // date stamp + CRC
            .Step(0, PacketBuilder.BuildDmpaftHeader(0)) // nPages=0
                                                         // Fallback DMPAFT: returns 1 page
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])               // "DMPAFT\n"
            .Step(6, [DavisProtocol.Ack])               // all-zeros date stamp + CRC
            .Step(0, PacketBuilder.BuildDmpaftHeader(1)) // nPages=1
            .Step(1, page)                               // ACK prompt → 267-byte page
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var records = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(since, fallbackOnEmpty: true))
            records.Add(r);

        records.Should().HaveCount(1);
        records[0].OutsideTemperatureF.Should().BeApproximately(72.0, 0.1);
        records[0].DateTimeLocal.Should().Be(recordTime);

        client.Dispose();
        await station.DisposeAsync();
    }

    [TestMethod]
    public async Task GetArchiveSinceAsync_ZeroPagesNonMinValue_WithoutFallback_YieldsNoRecords()
    {
        // Without fallbackOnEmpty the zero-pages response must be respected as-is.
        var since = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(7, [DavisProtocol.Ack])
            .Step(6, [DavisProtocol.Ack])
            .Step(0, PacketBuilder.BuildDmpaftHeader(0))
            .Start();

        var (client, station) = CreatePair(server.Port);
        await client.OpenAsync(CancellationToken.None);

        var records = new List<ArchiveRecord>();
        await foreach (var r in station.GetArchiveSinceAsync(since))
            records.Add(r);

        records.Should().BeEmpty();

        client.Dispose();
        await station.DisposeAsync();
    }
}

using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

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

    // ── GetCurrentConditionsAsync ─────────────────────────────────────────────

    [TestMethod]
    public async Task GetCurrentConditionsAsync_ReturnsDecodedLoop2Fields()
    {
        byte[] loop2 = PacketBuilder.BuildLoop2Packet(
            outsideTempF:     65.3,
            insideTempF:      71.0,
            outsideHumidity:  58,
            baroPressureInHg: 29.850,
            windSpeedMph:     5,
            windDirDeg:       180,
            dewPointF:        50.0);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()                              // wake before LPS
            .Step(8, [DavisProtocol.Ack, ..loop2])  // "LPS 1 1\n" (8 b) → ACK + 99-byte LOOP2
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
        byte[] loop2 = PacketBuilder.BuildLoop2Packet();

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Ack, ..loop2])
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
            .Step(8, [DavisProtocol.Ack, ..timeResp])           // "GETTIME\n" (8 b) → ACK + time
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

    // ── Concurrency — semaphore serialization ─────────────────────────────────

    [TestMethod]
    public async Task GetCurrentConditionsAsync_TwoConcurrentCalls_BothSucceed()
    {
        // Two GetCurrentConditionsAsync tasks race to acquire the internal
        // SemaphoreSlim(1,1).  Script two full wake+LPS interactions.
        byte[] loop2a = PacketBuilder.BuildLoop2Packet(outsideTempF: 65.0);
        byte[] loop2b = PacketBuilder.BuildLoop2Packet(outsideTempF: 70.0);

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Ack, ..loop2a]) // 1st call
            .WakeStep()
            .Step(8, [DavisProtocol.Ack, ..loop2b]) // 2nd call
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
            dateTime:               recordTime,
            outsideTempF:           68.5,
            highOutsideTempF:       72.0,
            lowOutsideTempF:        65.0,
            insideTempF:            74.0,
            outsideHumidity:        55,
            insideHumidity:         42,
            barometricPressureInHg: 29.850,
            windSpeedMph:           6,
            windGustMph:            12);

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
        byte[] rec0 = PacketBuilder.BuildArchiveDataBytes(dateTime: baseTime,               outsideTempF: 65.0);
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
}

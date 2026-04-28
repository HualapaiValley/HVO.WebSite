using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
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
}

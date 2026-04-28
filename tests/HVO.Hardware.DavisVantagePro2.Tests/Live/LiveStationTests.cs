using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.DavisVantagePro2.Tests.Live;

/// <summary>
/// Live integration tests that connect to a real Davis WeatherLink IP adapter.
///
/// These tests are skipped unless the <c>DAVIS_LIVE_HOST</c> environment variable
/// is set to the adapter's IP address or hostname.
///
/// <code>
///   # Run live tests only:
///   DAVIS_LIVE_HOST=192.168.2.121 dotnet test --filter "TestCategory=Live"
///
///   # Optional: override port (default 22222):
///   DAVIS_LIVE_PORT=22222
/// </code>
///
/// Live test scenarios covered:
/// <list type="bullet">
///   <item>Basic connect / disconnect lifecycle</item>
///   <item>GetCurrentConditions — field validity and physical plausibility</item>
///   <item>Response timing — reads should complete within 5 seconds</item>
///   <item>GetConsoleTime — within 24 hours of system clock</item>
///   <item>StreamLoop2Async — receive multiple consecutive packets</item>
///   <item>GetStationInfo — firmware version and hardware description non-empty</item>
///   <item>Concurrent reads on the same station — semaphore serialisation</item>
///   <item>Two simultaneous TCP clients — graceful handling</item>
///   <item>Disconnect then reconnect — second client succeeds</item>
/// </list>
/// </summary>
[TestClass]
[TestCategory("Live")]
public class LiveStationTests
{
    private static string? _host;
    private static int _port = 22222;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _host = Environment.GetEnvironmentVariable("DAVIS_LIVE_HOST");
        if (int.TryParse(Environment.GetEnvironmentVariable("DAVIS_LIVE_PORT"), out int p))
            _port = p;
    }

    private void SkipIfNotLive()
    {
        if (string.IsNullOrEmpty(_host))
            Assert.Inconclusive("Set DAVIS_LIVE_HOST to run live tests (e.g. DAVIS_LIVE_HOST=192.168.2.121).");
    }

    private (DavisConsoleClient client, VantageStation station) CreateStation() =>
        CreateStation(_host!, _port);

    private static (DavisConsoleClient client, VantageStation station) CreateStation(string host, int port)
    {
        var client  = new DavisConsoleClient(host, port, TimeSpan.FromSeconds(15),
                          NullLogger<DavisConsoleClient>.Instance);
        var station = new VantageStation(client, NullLogger<VantageStation>.Instance, maxTries: 3);
        return (client, station);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_Connect_Succeeds()
    {
        SkipIfNotLive();
        var (client, station) = CreateStation();
        try
        {
            await station.ConnectAsync();
            station.IsConnected.Should().BeTrue();
        }
        finally
        {
            await station.DisconnectAsync();
            await station.DisposeAsync();
            client.Dispose();
        }
    }

    // ── Current conditions ────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_GetCurrentConditions_ReturnsPhysicallyPlausibleValues()
    {
        SkipIfNotLive();
        var (client, station) = CreateStation();
        try
        {
            await station.ConnectAsync();
            var cond = await station.GetCurrentConditionsAsync();

            cond.Should().NotBeNull();
            cond.OutsideTemperatureF.Should().NotBeNull("outside temperature should be available");
            cond.BarometricPressureInHg.Should().NotBeNull("barometric pressure should be available");

            cond.OutsideTemperatureF!.Value.Should()
                .BeInRange(-60.0, 140.0, "temperature must be within sensor range");
            cond.BarometricPressureInHg!.Value.Should()
                .BeInRange(25.0, 32.0, "sea-level pressure must be within valid range");
        }
        finally
        {
            await station.DisconnectAsync();
            await station.DisposeAsync();
            client.Dispose();
        }
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_GetCurrentConditions_CompletesWithin5Seconds()
    {
        SkipIfNotLive();
        var (client, station) = CreateStation();
        try
        {
            await station.ConnectAsync();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var cond = await station.GetCurrentConditionsAsync();

            sw.Stop();
            cond.Should().NotBeNull();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
                "reading one LOOP2 packet should be fast");
        }
        finally
        {
            await station.DisconnectAsync();
            await station.DisposeAsync();
            client.Dispose();
        }
    }

    // ── Console time ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_GetConsoleTime_IsWithin24HoursOfSystemClock()
    {
        SkipIfNotLive();
        var (client, station) = CreateStation();
        try
        {
            await station.ConnectAsync();
            DateTime consoleTime = await station.GetConsoleTimeAsync();

            double diffHours = Math.Abs((DateTime.Now - consoleTime).TotalHours);
            diffHours.Should().BeLessThan(24,
                "console clock should not differ from system time by more than 24 hours");
        }
        finally
        {
            await station.DisconnectAsync();
            await station.DisposeAsync();
            client.Dispose();
        }
    }

    // ── LOOP2 streaming ───────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_StreamLoop2_ReceivesThreeConsecutivePackets()
    {
        SkipIfNotLive();
        var (client, station) = CreateStation();
        try
        {
            await station.ConnectAsync();

            var packets = new List<Loop2Packet>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await foreach (var p in station.StreamLoop2Async(3, cts.Token))
                packets.Add(p);

            packets.Should().HaveCount(3, "should receive exactly 3 LOOP2 packets");
            packets.Should().AllSatisfy(p =>
                p.OutsideTemperatureF.Should().NotBeNull("every packet should have outside temp"));
        }
        finally
        {
            await station.DisconnectAsync();
            await station.DisposeAsync();
            client.Dispose();
        }
    }

    // ── Station info ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_GetStationInfo_ReturnsFirmwareVersionAndHardwareDescription()
    {
        SkipIfNotLive();
        var (client, station) = CreateStation();
        try
        {
            await station.ConnectAsync();
            var info = await station.GetStationInfoAsync();

            info.Should().NotBeNull();
            info.FirmwareVersion.Should().NotBeNullOrEmpty("firmware version should be present");
            info.HardwareDescription.Should().NotBeNullOrEmpty("hardware description should be present");
        }
        finally
        {
            await station.DisconnectAsync();
            await station.DisposeAsync();
            client.Dispose();
        }
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_ConcurrentReadsOnSameStation_BothCompleteWithoutDeadlock()
    {
        SkipIfNotLive();
        var (client, station) = CreateStation();
        try
        {
            await station.ConnectAsync();

            // Fire two concurrent reads — the internal SemaphoreSlim(1,1) must
            // serialise them without deadlock or data corruption.
            var t1 = station.GetCurrentConditionsAsync();
            var t2 = station.GetCurrentConditionsAsync();
            var results = await Task.WhenAll(t1, t2);

            results.Should().HaveCount(2);
            results[0].Should().NotBeNull();
            results[1].Should().NotBeNull();
        }
        finally
        {
            await station.DisconnectAsync();
            await station.DisposeAsync();
            client.Dispose();
        }
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_TwoSimultaneousClients_HandledGracefully()
    {
        SkipIfNotLive();
        // The Davis console typically supports only one TCP client at a time.
        // The second client may be rejected (DavisException / timeout) or it may
        // succeed if the hardware accepts parallel connections.  Either way,
        // neither client should throw an unhandled exception.
        var (client1, station1) = CreateStation();
        var (client2, station2) = CreateStation();

        Exception? secondClientError = null;

        try
        {
            await station1.ConnectAsync();

            try
            {
                await station2.ConnectAsync();
                var cond2 = await station2.GetCurrentConditionsAsync();
                cond2.Should().NotBeNull();
            }
            catch (Exception ex) when (ex is DavisException or TimeoutException or OperationCanceledException)
            {
                // Expected: the console rejected the second connection
                secondClientError = ex;
            }

            // First client must still be operational regardless
            var cond1 = await station1.GetCurrentConditionsAsync();
            cond1.Should().NotBeNull();
        }
        finally
        {
            await station1.DisconnectAsync();
            await station1.DisposeAsync();
            client1.Dispose();

            try { await station2.DisconnectAsync(); } catch { }
            await station2.DisposeAsync();
            client2.Dispose();
        }

        // Log what happened to the second client (informational, not a failure)
        if (secondClientError is not null)
            Console.WriteLine($"Second client was rejected as expected: {secondClientError.GetType().Name}");
    }

    // ── Disconnect / reconnect ────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Live")]
    public async Task Live_DisconnectThenReconnectWithNewClient_SecondReadSucceeds()
    {
        SkipIfNotLive();

        // First session
        {
            var (client1, station1) = CreateStation();
            try
            {
                await station1.ConnectAsync();
                var r1 = await station1.GetCurrentConditionsAsync();
                r1.Should().NotBeNull();
                await station1.DisconnectAsync();
            }
            finally
            {
                await station1.DisposeAsync();
                client1.Dispose();
            }
        }

        // Second session — a fresh TCP connection should be accepted by the console
        {
            var (client2, station2) = CreateStation();
            try
            {
                await station2.ConnectAsync();
                var r2 = await station2.GetCurrentConditionsAsync();
                r2.Should().NotBeNull();
                r2.OutsideTemperatureF.Should().NotBeNull();
            }
            finally
            {
                await station2.DisconnectAsync();
                await station2.DisposeAsync();
                client2.Dispose();
            }
        }
    }
}

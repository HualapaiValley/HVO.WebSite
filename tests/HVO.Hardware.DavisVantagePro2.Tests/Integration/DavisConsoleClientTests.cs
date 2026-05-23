using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.DavisVantagePro2.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="DavisConsoleClient"/> using an in-process
/// <see cref="FakeDavisServer"/> on the loopback interface.
///
/// Each test creates a fresh server and client pair.  The fake server is
/// scripted with the exact bytes that the real Davis console would exchange.
///
/// Note: WakeAsync contains a hard 500 ms delay per attempt by design, so
/// these tests intentionally run a bit slower than pure unit tests.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class DavisConsoleClientTests
{
    private static DavisConsoleClient CreateClient(int port) =>
        new("127.0.0.1", port, TimeSpan.FromSeconds(1),
            NullLogger<DavisConsoleClient>.Instance);

    // ── Wake sequence ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task WakeAsync_WithValidResponse_DoesNotThrow()
    {
        await using var server = new FakeDavisServer();
        server.WakeStep().Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        Func<Task> act = () => client.WakeAsync(maxTries: 1);
        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task WakeAsync_WithWrongResponse_ThrowsDavisWakeupException()
    {
        await using var server = new FakeDavisServer();
        // Send wrong bytes instead of \n\r
        server.Step(4, [0xFF, 0xFF]).Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        Func<Task> act = () => client.WakeAsync(maxTries: 1);
        await act.Should().ThrowAsync<DavisWakeupException>();
    }

    // ── GetDataWithCrc16Async ─────────────────────────────────────────────────

    [TestMethod]
    public async Task GetDataWithCrc16Async_ValidCrcPacket_ReturnsFullBuffer()
    {
        // Build a 12-byte packet (10 data + 2 CRC) with a valid CRC
        byte[] rawData = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        byte[] packet = CrcCalculator.AppendCrc(rawData); // 12 bytes

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(0, packet)    // send the packet immediately after wake
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);
        await client.WakeAsync(maxTries: 1);

        byte[] result = await client.GetDataWithCrc16Async(12, CancellationToken.None);
        result.Should().Equal(packet);
    }

    [TestMethod]
    public async Task GetDataWithCrc16Async_InvalidCrc_ThrowsDavisCrcExceptionAfterRetries()
    {
        // Build a packet then corrupt the last CRC byte so validation always fails
        byte[] rawData = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A];
        byte[] badPacket = CrcCalculator.AppendCrc(rawData);
        badPacket[^1] ^= 0xFF; // corrupt

        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(0, badPacket) // attempt 1 — no NAK sent first
            .Step(1, badPacket) // attempt 2 — receive NAK (1 byte), re-send bad packet
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);
        await client.WakeAsync(maxTries: 1);

        Func<Task> act = () => client.GetDataWithCrc16Async(12, CancellationToken.None, maxTries: 2);
        await act.Should().ThrowAsync<DavisCrcException>();
    }

    [TestMethod]
    public async Task GetDataWithCrc16Async_ConnectionClosedDuringRead_PreservesConnectionFailure()
    {
        var server = new FakeDavisServer();
        bool disposed = false;
        server
            .WakeStep()
            .Start();

        try
        {
            using var client = CreateClient(server.Port);
            await client.OpenAsync(CancellationToken.None);
            await client.WakeAsync(maxTries: 1);

            Task<byte[]> readTask = client.GetDataWithCrc16Async(12, CancellationToken.None, maxTries: 1);
            await Task.Delay(100);
            await server.DisposeAsync();
            disposed = true;

            Func<Task> act = async () => await readTask;
            var exception = await act.Should().ThrowAsync<DavisException>();
            exception.WithMessage("Connection closed by console");
            exception.Which.StackTrace.Should().Contain("ReadExactAsync");
        }
        finally
        {
            if (!disposed)
                await server.DisposeAsync();
        }
    }

    // ── SendCommandAsync ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task SendCommandAsync_OkResponse_ReturnsDataLines()
    {
        // Caller is responsible for waking first; NVER\n = 5 bytes.
        byte[] response = PacketBuilder.BuildTextResponse("1.73");

        await using var server = new FakeDavisServer();
        server
            .Step(5, response)  // receive "NVER\n", send OK + version
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        string[] lines = await client.SendCommandAsync("NVER\n", CancellationToken.None, maxTries: 1);

        lines.Should().HaveCount(1);
        lines[0].Should().Be("1.73");
    }

    [TestMethod]
    public async Task SendCommandAsync_MultiLineResponse_ReturnsAllDataLines()
    {
        // Caller is responsible for waking first; BARDATA\n = 8 bytes.
        byte[] response = PacketBuilder.BuildTextResponse(
            "BAR  29.990",
            "Elevation  4500",
            "DEW POINT  55");

        await using var server = new FakeDavisServer();
        server
            .Step(8, response)
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        string[] lines = await client.SendCommandAsync("BARDATA\n", CancellationToken.None, maxTries: 1);

        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("BAR");
        lines[1].Should().StartWith("Elevation");
        lines[2].Should().StartWith("DEW POINT");
    }

    [TestMethod]
    public async Task SendCommandAsync_SplitResponseAcrossShortGap_ReturnsFullDataLines()
    {
        byte[] response = PacketBuilder.BuildTextResponse("1.73");

        await using var server = new FakeDavisServer();
        server
            .StepChunks(
                5,
                (response[..4], 450),
                (response[4..], 50))
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        string[] lines = await client.SendCommandAsync("NVER\n", CancellationToken.None, maxTries: 1);

        lines.Should().HaveCount(1);
        lines[0].Should().Be("1.73");
    }

    [TestMethod]
    public async Task SendCommandRawAsync_ReceiversResponse_ReturnsRawPayload()
    {
        byte[] response = PacketBuilder.BuildReceiversResponse(0b0000_0101);

        await using var server = new FakeDavisServer();
        server
            .Step(10, response)
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        byte[] raw = await client.SendCommandRawAsync("RECEIVERS\n", CancellationToken.None, maxTries: 1);

        raw.Should().Equal([0b0000_0101]);
    }

    [TestMethod]
    public async Task SendCommandRawAsync_SplitResponseAcrossShortGap_ReturnsCompletePayload()
    {
        byte[] response = PacketBuilder.BuildReceiversResponse(0b0000_0101);

        await using var server = new FakeDavisServer();
        server
            .StepChunks(
                10,
                (response[..6], 450),
                (response[6..], 50))
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        byte[] raw = await client.SendCommandRawAsync("RECEIVERS\n", CancellationToken.None, maxTries: 1);

        raw.Should().Equal([0b0000_0101]);
    }

    [TestMethod]
    public async Task SendCommandRawAsync_InvalidPrefix_ThrowsDavisProtocolException()
    {
        await using var server = new FakeDavisServer();
        server
            .Step(10, [(byte)'B', (byte)'A', (byte)'D', 0x0A, 0x0D, 0b0000_0101])
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        Func<Task> act = () => client.SendCommandRawAsync("RECEIVERS\n", CancellationToken.None, maxTries: 1);

        await act.Should().ThrowAsync<DavisProtocolException>();
    }

    [TestMethod]
    public async Task SendCommandUntilLineAsync_WaitsForDoneLine()
    {
        byte[] response = PacketBuilder.BuildTextResponse("DONE");

        await using var server = new FakeDavisServer();
        server
            .Step(7, response)
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);

        string[] lines = await client.SendCommandUntilLineAsync("CLRALM\n", "DONE", CancellationToken.None, maxTries: 1);

        lines.Should().Contain("DONE");
    }

    // ── SendDataAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SendDataAsync_WithAck_DoesNotThrow()
    {
        // GETTIME\n = 8 bytes; server responds with ACK only (data follows separately)
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Ack]) // receive "GETTIME\n", send ACK
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);
        await client.WakeAsync(maxTries: 1);

        Func<Task> act = () => client.SendDataAsync(
            System.Text.Encoding.ASCII.GetBytes("GETTIME\n"), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task SendDataAsync_WithLfButInvalidCr_ThrowsDavisProtocolException()
    {
        await using var server = new FakeDavisServer();
        server
            .WakeStep()
            .Step(8, [DavisProtocol.Lf, 0xFF, DavisProtocol.Ack])
            .Start();

        using var client = CreateClient(server.Port);
        await client.OpenAsync(CancellationToken.None);
        await client.WakeAsync(maxTries: 1);

        Func<Task> act = () => client.SendDataAsync(
            System.Text.Encoding.ASCII.GetBytes("GETTIME\n"), CancellationToken.None);

        await act.Should().ThrowAsync<DavisProtocolException>()
            .WithMessage("*Expected LF CR prefix*");
    }
}

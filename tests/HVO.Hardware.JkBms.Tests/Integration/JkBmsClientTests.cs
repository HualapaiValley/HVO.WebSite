using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.JkBms.Tests.Integration;

[TestClass]
public class JkBmsClientTests
{
    private const string TestAddress = "AA:BB:CC:DD:EE:FF";

    private static JkBmsClient CreateClient(FakeBmsTransport transport) =>
        new(transport, NullLogger<JkBmsClient>.Instance);

    // ── Success path ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PollCellInfoAsync_Success_ReturnsParsedPacket()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        var packet = await client.PollCellInfoAsync(CancellationToken.None);

        packet.Should().NotBeNull();
        packet.CellCount.Should().Be(15);
    }

    [TestMethod]
    public async Task PollCellInfoAsync_Success_ExchangeCalledOnce()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        await client.PollCellInfoAsync(CancellationToken.None);

        transport.ExchangeCallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task PollCellInfoAsync_CalledTwice_ExchangeCalledTwice()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        await client.PollCellInfoAsync(CancellationToken.None);
        await client.PollCellInfoAsync(CancellationToken.None);

        transport.ExchangeCallCount.Should().Be(2);
    }

    [TestMethod]
    public async Task DeviceAddress_ReturnsTransportAddress()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        client.DeviceAddress.Should().Be(TestAddress);
    }

    // ── Exchange failure propagation ─────────────────────────────────────────

    [TestMethod]
    public async Task PollCellInfoAsync_ExchangeThrows_ExceptionPropagated()
    {
        var exception = new InvalidOperationException("Simulated exchange failure");
        var transport = new FakeBmsTransport(TestAddress, exchangeException: exception);
        var client = CreateClient(transport);

        var act = async () => await client.PollCellInfoAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated exchange failure");
    }

    // ── Wrong frame type → retry ──────────────────────────────────────────────

    [TestMethod]
    public async Task PollCellInfoAsync_SettingsFrameFirst_RetriesAndReturnsCellInfo()
    {
        var settingsFrame = TestFrameBuilder.BuildMinimalFrame(JkBmsProtocol.FrameTypeSettings);
        var cellInfoFrame = TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        var transport = new FakeBmsTransport(
            TestAddress,
            frameSequence: [settingsFrame, cellInfoFrame]);
        var client = CreateClient(transport);

        var packet = await client.PollCellInfoAsync(CancellationToken.None);

        packet.CellCount.Should().Be(15);
        // Command sent once; second frame obtained via ReadNextFrameAsync (no re-command).
        transport.ExchangeCallCount.Should().Be(1);
        transport.ReadNextFrameCallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task PollCellInfoAsync_OnlyWrongTypeFrames_ThrowsJkBmsFrameException()
    {
        var wrongFrame = TestFrameBuilder.BuildMinimalFrame(JkBmsProtocol.FrameTypeSettings);
        // 5 wrong frames: 1 consumed by ExchangeAsync + 4 by ReadNextFrameAsync (MaxAttempts=5).
        var transport = new FakeBmsTransport(
            TestAddress,
            frameSequence: [wrongFrame, wrongFrame, wrongFrame, wrongFrame, wrongFrame]);
        var client = CreateClient(transport);

        var act = async () => await client.PollCellInfoAsync(CancellationToken.None);

        await act.Should().ThrowAsync<JkBmsFrameException>();
        transport.ExchangeCallCount.Should().Be(1);
        transport.ReadNextFrameCallCount.Should().Be(4);
    }

    [TestMethod]
    public async Task PollDeviceInfoAsync_ExchangeThrows_ExceptionPropagated()
    {
        var exception = new JkBmsConnectException(TestAddress, 1,
            new InvalidOperationException("BLE gone"));
        var transport = new FakeBmsTransport(TestAddress, exchangeException: exception);
        var client = CreateClient(transport);

        var act = async () => await client.PollDeviceInfoAsync(CancellationToken.None);

        await act.Should().ThrowAsync<JkBmsConnectException>();
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PollCellInfoAsync_CancelledBeforeExchange_ThrowsOperationCanceledException()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.PollCellInfoAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DisposeAsync_DisposesTransport()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        await client.DisposeAsync();

        transport.DisposeCallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task DisposeAsync_CalledTwice_DisposesTransportOnce()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        await client.DisposeAsync();
        await client.DisposeAsync();

        transport.DisposeCallCount.Should().Be(1);
    }
}

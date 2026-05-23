using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.JkBms.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
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

    // ── Frame type tolerance ──────────────────────────────────────────────────

    [TestMethod]
    public async Task PollCellInfoAsync_SettingsFrameResponse_CallsExchangeOnceAndAttemptsParse()
    {
        // Verifies the client no longer rejects frames based on type — it sends the
        // command once and attempts to parse whatever the BMS returns.
        // An all-zeros settings frame will fail CellInfoPacket.Parse (bitmask=0 is invalid),
        // but the critical assertion is ExchangeCallCount == 1 (no re-command on wrong type).
        var settingsFrame = TestFrameBuilder.BuildMinimalFrame(JkBmsProtocol.FrameTypeSettings);
        var transport = new FakeBmsTransport(TestAddress, responseFrame: settingsFrame);
        var client = CreateClient(transport);

        await FluentActions.Invoking(() => client.PollCellInfoAsync(CancellationToken.None))
            .Should().ThrowAsync<JkBmsFrameException>();

        transport.ExchangeCallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task PollCellInfoAsync_AnyValidFrame_CallsExchangeExactlyOnce()
    {
        var transport = new FakeBmsTransport(TestAddress);
        var client = CreateClient(transport);

        await client.PollCellInfoAsync(CancellationToken.None);

        // Regardless of frame type, command is sent exactly once.
        transport.ExchangeCallCount.Should().Be(1);
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

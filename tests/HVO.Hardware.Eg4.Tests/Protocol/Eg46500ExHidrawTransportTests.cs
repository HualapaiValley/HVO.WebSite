using FluentAssertions;
using HVO.Hardware.Eg4.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace HVO.Hardware.Eg4.Tests.Protocol;

[TestClass]
public sealed class Eg46500ExHidrawTransportTests
{
    [TestMethod]
    public async Task Exchange_WritesOnlyEncodedInquiryAndReassemblesReportsThroughCr()
    {
        var response = FrameResponse("(PI30").Concat(new byte[] { 0, 0, 0 }).ToArray();
        var device = new ScriptedDevice(response, maximumReadSize: 3);
        await using var transport = new Eg46500ExHidrawTransport(device, TimeProvider.System, TimeSpan.FromSeconds(1));

        var actual = await transport.ExchangeAsync(Eg46500ExInquiry.ProtocolId, CancellationToken.None);

        device.Written.Should().Equal(Eg46500ExPi30Protocol.Encode(Eg46500ExInquiry.ProtocolId));
        actual.Should().Equal(FrameResponse("(PI30"));
        Eg46500ExPi30Protocol.DecodePayload(actual).Should().Be("PI30");
    }

    [TestMethod]
    public async Task Exchange_RejectsDisconnectedAndOversizedResponses()
    {
        await using var disconnected = new Eg46500ExHidrawTransport(
            new ScriptedDevice([]), TimeProvider.System, TimeSpan.FromSeconds(1));
        var disconnectedFailure = await FluentActions.Awaiting(async () =>
                await disconnected.ExchangeAsync(Eg46500ExInquiry.ProtocolId, CancellationToken.None))
            .Should().ThrowAsync<Eg4TransportException>();
        disconnectedFailure.Which.Kind.Should().Be(Eg4TransportFailureKind.Disconnected);

        await using var oversized = new Eg46500ExHidrawTransport(
            new ScriptedDevice(Enumerable.Repeat((byte)'A', 512).ToArray()), TimeProvider.System, TimeSpan.FromSeconds(1));
        var oversizedFailure = await FluentActions.Awaiting(async () =>
                await oversized.ExchangeAsync(Eg46500ExInquiry.ProtocolId, CancellationToken.None))
            .Should().ThrowAsync<Eg4TransportException>();
        oversizedFailure.Which.Kind.Should().Be(Eg4TransportFailureKind.MalformedFrame);
    }

    [TestMethod]
    public async Task Exchange_DistinguishesPostStartTimeoutFromCallerCancellation()
    {
        var time = new FakeTimeProvider();
        var timeoutDevice = new BlockingDevice();
        await using var timeoutTransport = new Eg46500ExHidrawTransport(timeoutDevice, time, TimeSpan.FromMinutes(1));
        var timedOut = timeoutTransport.ExchangeAsync(Eg46500ExInquiry.ProtocolId, CancellationToken.None).AsTask();
        timeoutDevice.ReadStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        time.Advance(TimeSpan.FromMinutes(1));
        var timeoutFailure = await FluentActions.Awaiting(() => timedOut).Should().ThrowAsync<Eg4TransportException>();
        timeoutFailure.Which.Kind.Should().Be(Eg4TransportFailureKind.Timeout);

        var canceledDevice = new BlockingDevice();
        await using var canceledTransport = new Eg46500ExHidrawTransport(canceledDevice, TimeProvider.System, TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        var canceled = canceledTransport.ExchangeAsync(Eg46500ExInquiry.ProtocolId, cancellation.Token).AsTask();
        canceledDevice.ReadStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        await cancellation.CancelAsync();
        await FluentActions.Awaiting(() => canceled).Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task Dispose_WaitsForActiveExchangeAndRejectsNewWork()
    {
        var device = new BlockingDevice();
        var transport = new Eg46500ExHidrawTransport(device, TimeProvider.System, TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        var exchange = transport.ExchangeAsync(Eg46500ExInquiry.ProtocolId, cancellation.Token).AsTask();
        device.ReadStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

        var dispose = transport.DisposeAsync().AsTask();
        dispose.IsCompleted.Should().BeFalse();
        await cancellation.CancelAsync();
        await FluentActions.Awaiting(() => exchange).Should().ThrowAsync<OperationCanceledException>();
        await dispose;

        device.Disposed.Should().BeTrue();
        await FluentActions.Awaiting(async () =>
                await transport.ExchangeAsync(Eg46500ExInquiry.ProtocolId, CancellationToken.None))
            .Should().ThrowAsync<ObjectDisposedException>();
        await transport.DisposeAsync();
    }

    [TestMethod]
    public void Factory_ClassifiesMissingEndpointAsDisconnectedAndPreservesCause()
    {
        var factory = new Eg46500ExHidrawTransportFactory(TimeProvider.System);

        var failure = FluentActions.Invoking(() => factory.Create("/path/that/does/not/exist"))
            .Should().Throw<Eg4TransportException>();

        failure.Which.Kind.Should().Be(Eg4TransportFailureKind.Disconnected);
        failure.Which.InnerException.Should().BeOfType<IOException>();
    }

    private static byte[] FrameResponse(string payload)
    {
        var payloadBytes = System.Text.Encoding.ASCII.GetBytes(payload);
        ushort crc = 0;
        foreach (var value in payloadBytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        static byte Adjust(byte value) => value is 0x28 or 0x0D or 0x0A or 0x00 ? (byte)(value + 1) : value;
        return [.. payloadBytes, Adjust((byte)(crc >> 8)), Adjust((byte)crc), 0x0D];
    }

    private sealed class ScriptedDevice(byte[] response, int maximumReadSize = 8) : IEg46500ExHidrawDevice
    {
        private readonly Queue<byte> _response = new(response);
        private readonly MemoryStream _written = new();
        public byte[] Written => _written.ToArray();
        public void Write(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _written.Write(bytes);
        }
        public int Read(Span<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(buffer.Length, maximumReadSize), _response.Count);
            for (var index = 0; index < count; index++) buffer[index] = _response.Dequeue();
            return count;
        }
        public void Dispose() => _written.Dispose();
    }

    private sealed class BlockingDevice : IEg46500ExHidrawDevice
    {
        public ManualResetEventSlim ReadStarted { get; } = new();
        public bool Disposed { get; private set; }
        public void Write(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken) => cancellationToken.ThrowIfCancellationRequested();
        public int Read(Span<byte> buffer, CancellationToken cancellationToken)
        {
            ReadStarted.Set();
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }
        public void Dispose()
        {
            Disposed = true;
            ReadStarted.Dispose();
        }
    }
}

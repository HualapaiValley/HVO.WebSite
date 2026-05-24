using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Tests.Fakes;
using Linux.Bluetooth;

namespace HVO.Hardware.JkBms.Tests.Fakes;

/// <summary>
/// Scriptable fake implementation of <see cref="IBmsTransport"/> for unit and integration tests.
///
/// This transport models a persistent connection — create one instance and reuse it across
/// multiple <see cref="ExchangeAsync"/> calls, matching how the production transport is used.
///
/// Usage:
///   1. Create the fake via the constructor, optionally specifying exchange failures and responses.
///   2. Pass it directly to <see cref="JkBmsClient"/> (or wrap in <see cref="FakeBmsTransportFactory"/>).
///   3. Execute the code under test.
///   4. Assert on <see cref="ConnectCallCount"/>, <see cref="ExchangeCallCount"/>, etc.
/// </summary>
public sealed class FakeBmsTransport : IBmsTransport
{
    private readonly byte[]? _responseFrame;
    private readonly Exception? _exchangeException;
    private readonly Queue<byte[]> _frameQueue;

    public string DeviceAddress { get; }
    public bool IsConnected { get; private set; }

    /// <summary>Always null in the fake — tests that need a settings frame should set up the client differently.</summary>
    public byte[]? LastSettingsFrame => null;

    public int ConnectCallCount { get; private set; }
    public int DisconnectCallCount { get; private set; }
    public int ExchangeCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    /// <summary>
    /// Create a fake transport.
    /// </summary>
    /// <param name="deviceAddress">Device address to report.</param>
    /// <param name="responseFrame">
    ///   Bytes returned by <see cref="ExchangeAsync"/> when no <paramref name="frameSequence"/> is set.
    ///   When null, a valid cell-info frame for a 15-cell pack is returned by default.
    /// </param>
    /// <param name="exchangeException">
    ///   When non-null, <see cref="ExchangeAsync"/> throws this exception instead of returning a frame.
    /// </param>
    /// <param name="frameSequence">
    ///   When set, frames are returned in order for successive <see cref="ExchangeAsync"/> calls.
    ///   Once the sequence is exhausted, falls back to <paramref name="responseFrame"/> or the default.
    /// </param>
    public FakeBmsTransport(
        string deviceAddress = "AA:BB:CC:DD:EE:FF",
        byte[]? responseFrame = null,
        Exception? exchangeException = null,
        IEnumerable<byte[]>? frameSequence = null)
    {
        DeviceAddress = deviceAddress;
        _responseFrame = responseFrame;
        _exchangeException = exchangeException;
        _frameQueue = new Queue<byte[]>(frameSequence ?? []);
    }

    public Task ConnectWithDeviceAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ConnectCallCount++;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        DisconnectCallCount++;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<byte[]> ExchangeAsync(byte[] command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ExchangeCallCount++;

        if (_exchangeException is not null)
            throw _exchangeException;

        var frame = _frameQueue.Count > 0
            ? _frameQueue.Dequeue()
            : _responseFrame ?? TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
        return Task.FromResult(frame);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Factory that creates <see cref="FakeBmsTransport"/> instances for use in worker-level tests.
/// </summary>
public sealed class FakeBmsTransportFactory : IBmsTransportFactory
{
    private readonly Func<string, FakeBmsTransport> _factory;

    public FakeBmsTransportFactory(Func<string, FakeBmsTransport> factory)
    {
        _factory = factory;
    }

    /// <summary>Create a factory that always returns a new transport with default settings (always succeeds).</summary>
    public static FakeBmsTransportFactory AlwaysSucceed() =>
        new(_ => new FakeBmsTransport());

    /// <summary>The adapter name passed to the last <see cref="Create"/> call (for assertion).</summary>
    public string? LastAdapterName { get; private set; }

    public IBmsTransport Create(string address, string adapterName)
    {
        LastAdapterName = adapterName;
        return _factory(address);
    }
}

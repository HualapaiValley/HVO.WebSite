using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Tests.Fakes;

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

    public string DeviceAddress { get; }
    public bool IsConnected { get; private set; }

    public int ConnectCallCount { get; private set; }
    public int DisconnectCallCount { get; private set; }
    public int ExchangeCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    /// <summary>
    /// Create a fake transport.
    /// </summary>
    /// <param name="deviceAddress">Device address to report.</param>
    /// <param name="responseFrame">
    ///   Bytes returned by <see cref="ExchangeAsync"/>. When null, a valid cell-info frame
    ///   for a 15-cell pack is returned by default.
    /// </param>
    /// <param name="exchangeException">
    ///   When non-null, <see cref="ExchangeAsync"/> throws this exception instead of returning a frame.
    /// </param>
    public FakeBmsTransport(
        string deviceAddress = "AA:BB:CC:DD:EE:FF",
        byte[]? responseFrame = null,
        Exception? exchangeException = null)
    {
        DeviceAddress = deviceAddress;
        _responseFrame = responseFrame;
        _exchangeException = exchangeException;
    }

    public Task ConnectAsync(CancellationToken ct)
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

        var frame = _responseFrame ?? TestFrameBuilder.BuildCellInfoFrame(cellCount: 15);
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

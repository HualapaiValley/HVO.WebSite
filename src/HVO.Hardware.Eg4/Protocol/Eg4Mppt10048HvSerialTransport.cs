using System.IO.Ports;

namespace HVO.Hardware.Eg4.Protocol;

public sealed class Eg4Mppt10048HvSerialTransportFactory(TimeProvider timeProvider) : IEg4RegisterTransportFactory
{
    public IEg4RegisterTransport Create(string port) =>
        new Eg4Mppt10048HvSerialTransport(port, timeProvider, TimeSpan.FromSeconds(2));
}

public sealed class Eg4Mppt10048HvSerialTransport : IEg4RegisterTransport
{
    private const int ResponseLength = 41;
    internal const int BaudRate = 9600;
    internal const int DataBits = 8;
    internal const Parity SerialParity = Parity.None;
    internal const StopBits SerialStopBits = StopBits.One;
    private readonly string _port;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private bool _disposed;

    internal Eg4Mppt10048HvSerialTransport(string port, TimeProvider timeProvider, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(port);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _port = port;
        _timeProvider = timeProvider;
        _timeout = timeout;
    }

    public async ValueTask<Eg4ReadRegistersResponse> ReadRegistersAsync(
        Eg4ReadRegistersRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Eg4Mppt10048HvProtocol.RequireFixedRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(_timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var serial = new SerialPort(_port, BaudRate, SerialParity, DataBits, SerialStopBits)
            {
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
            };
            serial.Open();
            serial.DiscardInBuffer();
            var requestFrame = Eg4Mppt10048HvProtocol.EncodeRequest(request);
            await serial.BaseStream.WriteAsync(requestFrame, linked.Token);
            await serial.BaseStream.FlushAsync(linked.Token);
            var response = new byte[ResponseLength];
            await serial.BaseStream.ReadExactlyAsync(response, linked.Token);
            return Eg4Mppt10048HvProtocol.DecodeResponse(request, response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new Eg4TransportException(Eg4TransportFailureKind.Timeout, "The MPPT controller did not return a complete response before timeout.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new Eg4TransportException(Eg4TransportFailureKind.Disconnected, "The MPPT serial exchange failed.", exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}

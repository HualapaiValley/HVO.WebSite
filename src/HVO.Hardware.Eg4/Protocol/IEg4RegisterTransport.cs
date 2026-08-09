namespace HVO.Hardware.Eg4.Protocol;

public interface IEg4RegisterTransport : IAsyncDisposable
{
    ValueTask<Eg4ReadRegistersResponse> ReadRegistersAsync(
        Eg4ReadRegistersRequest request,
        CancellationToken cancellationToken);
}

public interface IEg4RegisterTransportFactory
{
    IEg4RegisterTransport Create(string port);
}

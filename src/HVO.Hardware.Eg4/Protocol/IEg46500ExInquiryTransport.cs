namespace HVO.Hardware.Eg4.Protocol;

public interface IEg46500ExInquiryTransport : IAsyncDisposable
{
    ValueTask<byte[]> ExchangeAsync(Eg46500ExInquiry inquiry, CancellationToken cancellationToken);
}

public interface IEg46500ExInquiryTransportFactory
{
    IEg46500ExInquiryTransport Create(string port);
}

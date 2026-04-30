using HVO.Hardware.JkBms.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// Default factory that creates <see cref="JkBmsBluetoothTransport"/> instances.
/// Reads <see cref="JkBmsOptions"/> to determine the connect timeout.
/// </summary>
internal sealed class JkBmsBluetoothTransportFactory : IBmsTransportFactory
{
    private readonly IOptions<JkBmsOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    public JkBmsBluetoothTransportFactory(
        IOptions<JkBmsOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _loggerFactory = loggerFactory;
    }

    public IBmsTransport Create(string address, string adapterName)
    {
        var timeout = TimeSpan.FromSeconds(_options.Value.ConnectTimeoutSeconds);
        var logger = _loggerFactory.CreateLogger<JkBmsBluetoothTransport>();
        return new JkBmsBluetoothTransport(address, adapterName, timeout, logger);
    }
}

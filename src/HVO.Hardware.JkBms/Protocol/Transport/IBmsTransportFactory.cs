namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// Factory for creating <see cref="IBmsTransport"/> instances.
/// Registered as a singleton; each call to <see cref="Create"/> returns a new,
/// unconnected transport for the given device.
/// </summary>
public interface IBmsTransportFactory
{
    /// <summary>
    /// Create a new unconnected transport for the device at <paramref name="address"/>
    /// using the specified HCI adapter.
    /// </summary>
    /// <param name="address">Bluetooth MAC address (e.g. "C8:47:8C:E4:58:37").</param>
    /// <param name="adapterName">BlueZ HCI adapter name (e.g. "hci0").</param>
    IBmsTransport Create(string address, string adapterName);
}

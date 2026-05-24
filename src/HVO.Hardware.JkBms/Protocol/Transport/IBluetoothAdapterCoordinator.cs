using Linux.Bluetooth;

namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// Coordinates BLE discovery and connection for a BlueZ adapter.
///
/// The coordinator serializes connect attempts per adapter and performs only the scan work
/// needed to obtain a BlueZ <see cref="Device"/> for a specific target address while
/// keeping discovery active through the connection callback.
/// </summary>
public interface IBluetoothAdapterCoordinator
{
    /// <summary>
    /// Starts or reuses discovery on <paramref name="adapterName"/>, resolves the target
    /// <see cref="Device"/> for <paramref name="address"/>, and invokes
    /// <paramref name="connectAsync"/> before discovery is stopped.
    ///
    /// Only one callback is active at a time per adapter so BlueZ does not receive
    /// overlapping LE connection attempts.
    /// </summary>
    Task ConnectAsync(
        string adapterName,
        string address,
        Func<Device, CancellationToken, Task> connectAsync,
        CancellationToken ct);
}

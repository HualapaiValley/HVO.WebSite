using Linux.Bluetooth;

namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// Coordinates BLE discovery and connection for a BlueZ adapter.
///
/// The coordinator serializes connect attempts per adapter and performs only the scan work
/// needed to obtain a fresh BlueZ <see cref="Device"/> for a specific target address.
/// </summary>
public interface IBluetoothAdapterCoordinator
{
    /// <summary>
    /// Resolves a fresh BlueZ <see cref="Device"/> for <paramref name="address"/> on
    /// <paramref name="adapterName"/> and invokes <paramref name="connectAsync"/> while
    /// preserving the single-connect-at-a-time rule for that adapter.
    /// </summary>
    Task ConnectAsync(
        string adapterName,
        string address,
        Func<Device, CancellationToken, Task> connectAsync,
        CancellationToken ct);
}

/// <summary>
/// Wraps a BlueZ Device object returned from the scan loop.
/// Kept as a thin wrapper for future extensibility; Dispose is a no-op.
/// </summary>
public sealed class BluetoothDeviceLease(Device device) : IDisposable
{
    public Device Device { get; } = device;
    public void Dispose() { }
}

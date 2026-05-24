using System.Collections.Concurrent;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// Resolves one BLE connection candidate at a time per adapter.
///
/// Key constraint: BlueZ can only make ONE HCI LE connection attempt at a time on a
/// single controller. Parallel calls to Device.ConnectAsync() all result in
/// le-connection-abort-by-local.
///
/// Strategy:
///   1. Per adapter, use a semaphore so only one connect resolution is active at a time.
///   2. Look for an already-known BlueZ device first.
///   3. If needed, scan briefly until the target address advertises.
///   4. Invoke the caller's connect callback with the fresh BlueZ Device.
 /// </summary>
internal sealed class BluetoothAdapterCoordinator(ILogger<BluetoothAdapterCoordinator> logger) : IBluetoothAdapterCoordinator
{
    private static readonly TimeSpan ScanWindowDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(3);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AdapterLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task ConnectAsync(
        string adapterName,
        string address,
        Func<Device, CancellationToken, Task> connectAsync,
        CancellationToken ct)
    {
        var adapterLock = AdapterLocks.GetOrAdd(adapterName, _ => new SemaphoreSlim(1, 1));
        await adapterLock.WaitAsync(ct);

        try
        {
            await ConnectCoreAsync(adapterName, address, connectAsync, ct);
        }
        finally
        {
            adapterLock.Release();
        }
    }

    private async Task ConnectCoreAsync(
        string adapterName,
        string address,
        Func<Device, CancellationToken, Task> connectAsync,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Bluetooth connect resolution starting on {Adapter} for {Address}",
            adapterName,
            address);

        var adapter = await GetAdapterAsync(adapterName);

        var device = await ResolveDeviceAsync(adapter, adapterName, address, ct);

        logger.LogInformation(
            "Bluetooth adapter {Adapter} connecting to {Address}",
            adapterName,
            address);

        try
        {
            await connectAsync(device, ct);
            logger.LogDebug(
                "Connect callback completed for {Address} on {Adapter}",
                address,
                adapterName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Connect callback failed for {Address} on {Adapter}",
                address,
                adapterName);
            await Task.Delay(ConnectRetryDelay, ct);
            throw;
        }
    }

    private async Task<Device> ResolveDeviceAsync(Adapter adapter, string adapterName, string address, CancellationToken ct)
    {
        var knownDevices = await adapter.GetDevicesAsync();
        foreach (var knownDevice in knownDevices)
        {
            if (string.Equals(await knownDevice.GetAddressAsync(), address, StringComparison.OrdinalIgnoreCase))
                return knownDevice;
        }

        logger.LogDebug(
            "Scanning on {Adapter} for {Address}",
            adapterName,
            address);

        var seenDevice = new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnDeviceFound(Adapter sender, DeviceFoundEventArgs args)
        {
            _ = Task.Run(async () =>
            {
                var foundAddress = MacFromObjectPath(args.Device.ObjectPath.ToString());
                if (string.Equals(foundAddress, address, StringComparison.OrdinalIgnoreCase))
                    seenDevice.TrySetResult(args.Device);
            }, CancellationToken.None);

            return Task.CompletedTask;
        }

        adapter.DeviceFound += OnDeviceFound;
        try
        {
            await TryStartDiscoveryAsync(adapter, adapterName, ct);
            using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            scanCts.CancelAfter(ScanWindowDuration);
            return await seenDevice.Task.WaitAsync(scanCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out discovering {address} on {adapterName}.");
        }
        finally
        {
            adapter.DeviceFound -= OnDeviceFound;
            await TryStopDiscoveryAsync(adapter, adapterName, CancellationToken.None);
        }
    }

    private static string? MacFromObjectPath(string path)
    {
        const string prefix = "/dev_";
        var idx = path.LastIndexOf(prefix, StringComparison.Ordinal);
        return idx < 0 ? null : path[(idx + prefix.Length)..].Replace('_', ':');
    }

    private async Task TryStartDiscoveryAsync(Adapter adapter, string adapterName, CancellationToken ct)
    {
        try
        {
            await adapter.StartDiscoveryAsync();
        }
        catch (Tmds.DBus.DBusException ex) when (ex.ErrorName is
            "org.bluez.Error.InProgress" or "org.bluez.Error.NotReady")
        {
            logger.LogDebug(
                "Bluetooth adapter {Adapter} scan already running or not ready: {Msg}",
                adapterName, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Bluetooth adapter {Adapter} failed to start scan", adapterName);
        }
    }

    private async Task TryStopDiscoveryAsync(Adapter adapter, string adapterName, CancellationToken ct)
    {
        try
        {
            await adapter.StopDiscoveryAsync();
        }
        catch (Tmds.DBus.DBusException ex) when (ex.ErrorName is
            "org.bluez.Error.Failed" or "org.bluez.Error.NotReady")
        {
            logger.LogDebug(
                "Bluetooth adapter {Adapter} stop discovery: {Msg}", adapterName, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Non-fatal error stopping scan on {Adapter}", adapterName);
        }
    }

    private static async Task<Adapter> GetAdapterAsync(string adapterName)
    {
        var adapters = await BlueZManager.GetAdaptersAsync();
        return adapters.FirstOrDefault(
                a => a.ObjectPath.ToString().EndsWith("/" + adapterName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Bluetooth adapter '{adapterName}' not found. " +
                $"Available: {string.Join(", ", adapters.Select(a => a.ObjectPath.ToString().Split('/').Last()))}");
    }
}

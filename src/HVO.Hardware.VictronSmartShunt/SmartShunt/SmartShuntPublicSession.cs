using HVO.Hardware.VictronSmartShunt.Configuration;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Microsoft.Extensions.Options;
using Tmds.DBus;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public sealed class SmartShuntPublicSession : BackgroundService, ISmartShuntSessionState
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(2);

    private readonly SmartShuntOptions _options;
    private readonly ILogger<SmartShuntPublicSession> _logger;
    private readonly object _stateLock = new();

    private volatile SmartShuntLiveSample? _currentSample;

    public SmartShuntLiveSample? CurrentSample => _currentSample;

    public SmartShuntPublicSession(IOptions<SmartShuntOptions> optionsAccessor, ILogger<SmartShuntPublicSession> logger)
    {
        _options = optionsAccessor.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Address))
        {
            _logger.LogWarning("SmartShunt address is not configured; public session is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SmartShunt public session loop failed; retrying.");
                try
                {
                    await Task.Delay(ConnectRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        var adapter = await BlueZManager.GetAdapterAsync(_options.Adapter);
        await using var session = new DeviceSession(_options.Address, _logger, TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds));
        await session.ConnectAsync(adapter, ct);

        var fieldValues = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var startedNotifications = new List<IGattCharacteristic1>();
        var watchers = new List<IDisposable>();
        using var keepAliveTimer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PublicKeepAliveIntervalSeconds));

        try
        {
            await SendPublicKeepAliveAsync(session, ct);

            foreach (var field in SmartShuntPublicProtocol.Fields)
            {
                var characteristic = await session.GetCharacteristicAsync(field.Uuid, ct);
                watchers.Add(await characteristic.WatchPropertiesAsync(changes =>
                {
                    foreach (var pair in changes.Changed)
                    {
                        if (pair.Key != "Value" || pair.Value is not byte[] value)
                            continue;

                        lock (_stateLock)
                            fieldValues[field.Key] = value;

                        PublishCurrentSample(fieldValues);
                    }
                }));

                try
                {
                    await characteristic.StartNotifyAsync();
                    startedNotifications.Add(characteristic);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Notify start failed for SmartShunt field {FieldKey}", field.Key);
                }

                var initial = await characteristic.ReadValueAsync(new Dictionary<string, object>());
                lock (_stateLock)
                    fieldValues[field.Key] = initial;
            }

            PublishCurrentSample(fieldValues);

            while (await keepAliveTimer.WaitForNextTickAsync(ct))
            {
                await SendPublicKeepAliveAsync(session, ct);
            }
        }
        finally
        {
            foreach (var watcher in watchers)
                watcher.Dispose();

            foreach (var characteristic in startedNotifications)
            {
                try
                {
                    await characteristic.StopNotifyAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "StopNotifyAsync failed during SmartShunt session cleanup");
                }
            }
        }
    }

    private void PublishCurrentSample(Dictionary<string, byte[]> fieldValues)
    {
        Dictionary<string, byte[]> snapshot;
        lock (_stateLock)
            snapshot = fieldValues.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        _currentSample = SmartShuntPublicProtocol.DecodeSample(snapshot, DateTime.UtcNow);
    }

    private static async Task SendPublicKeepAliveAsync(DeviceSession session, CancellationToken ct)
    {
        var characteristic = await session.GetCharacteristicAsync(SmartShuntPublicProtocol.PublicKeepAliveUuid, ct);
        ct.ThrowIfCancellationRequested();
        await characteristic.WriteValueAsync(SmartShuntPublicProtocol.PublicKeepAlivePayload, new Dictionary<string, object>());
    }

    private sealed class DeviceSession(string address, ILogger logger, TimeSpan connectTimeout) : IAsyncDisposable
    {
        private Device? _device;

        public Device Device => _device ?? throw new InvalidOperationException("Session is not connected.");
        public string Address { get; } = address;

        public async Task ConnectAsync(Adapter adapter, CancellationToken ct)
        {
            _device = await FindDeviceAsync(adapter, Address, ct);

            Exception? lastError = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    logger.LogInformation("SmartShunt connect attempt {Attempt} to {Address}", attempt, Address);
                    await _device.ConnectAsync().WaitAsync(connectTimeout, ct);
                    await _device.WaitForPropertyValueAsync("Connected", true, connectTimeout);
                    await _device.WaitForPropertyValueAsync("ServicesResolved", true, connectTimeout);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    logger.LogWarning(ex, "SmartShunt connect attempt {Attempt} failed for {Address}", attempt, Address);

                    try
                    {
                        await _device.DisconnectAsync();
                    }
                    catch (Exception disconnectEx)
                    {
                        logger.LogDebug(disconnectEx, "DisconnectAsync failed during SmartShunt connect retry for {Address}", Address);
                    }

                    if (attempt < 3)
                        await Task.Delay(ConnectRetryDelay, ct);
                }
            }

            throw new InvalidOperationException($"Failed to connect to {Address}.", lastError);
        }

        public async Task<IGattCharacteristic1> GetCharacteristicAsync(string characteristicUuid, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var services = await Device.GetServicesAsync() ?? [];
            foreach (var service in services)
            {
                var characteristics = await service.GetCharacteristicsAsync() ?? [];
                foreach (var characteristic in characteristics)
                {
                    var uuid = await characteristic.GetUUIDAsync();
                    if (string.Equals(uuid, characteristicUuid, StringComparison.OrdinalIgnoreCase))
                        return characteristic;
                }
            }

            throw new InvalidOperationException($"Characteristic {characteristicUuid} not found.");
        }

        public async ValueTask DisposeAsync()
        {
            if (_device is null)
                return;

            try
            {
                await _device.DisconnectAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "DisconnectAsync failed during SmartShunt DeviceSession disposal for {Address}", Address);
            }

            _device = null;
        }

        private async Task<Device> FindDeviceAsync(Adapter adapter, string address, CancellationToken ct)
        {
            var known = await adapter.GetDevicesAsync();
            foreach (var device in known)
            {
                if (string.Equals(await device.GetAddressAsync(), address, StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            var tcs = new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task OnDeviceFound(Adapter sender, DeviceFoundEventArgs eventArgs)
            {
                Task.Run(async () =>
                {
                    if (string.Equals(await eventArgs.Device.GetAddressAsync(), address, StringComparison.OrdinalIgnoreCase))
                        tcs.TrySetResult(eventArgs.Device);
                }, ct);

                return Task.CompletedTask;
            }

            adapter.DeviceFound += OnDeviceFound;
            try
            {
                await adapter.StartDiscoveryAsync();
                using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                scanCts.CancelAfter(ScanTimeout);
                return await tcs.Task.WaitAsync(scanCts.Token);
            }
            finally
            {
                adapter.DeviceFound -= OnDeviceFound;
                try
                {
                    await adapter.StopDiscoveryAsync();
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "SmartShunt StopDiscoveryAsync failed during public-session cleanup for {Address}", address);
                }
            }
        }
    }
}

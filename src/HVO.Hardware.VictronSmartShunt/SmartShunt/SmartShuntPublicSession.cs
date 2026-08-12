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
    private volatile string? _lastError;
    private volatile int _isConnected;

    public SmartShuntLiveSample? CurrentSample => _currentSample;
    public bool IsConnected => _isConnected != 0;
    public string? LastError => _lastError;

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
                _lastError = ex.GetType().Name;
                _isConnected = 0;
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
        var operationTimeout = TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds);
        var adapter = await BlueZManager.GetAdapterAsync(_options.Adapter).WaitAsync(operationTimeout, ct);
        await using var session = new DeviceSession(_options.Address, _logger, TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds));
        await session.ConnectAsync(adapter, ct);
        _isConnected = 1;
        _lastError = null;
        _currentSample = null;

        var aggregate = new SmartShuntPublicAggregate();
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

                        PublishField(aggregate, field.Key, value, DateTime.UtcNow);
                    }
                }).WaitAsync(operationTimeout, ct));

                try
                {
                    await characteristic.StartNotifyAsync().WaitAsync(operationTimeout, ct);
                    startedNotifications.Add(characteristic);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Notify start failed for SmartShunt field {FieldKey}", field.Key);
                }

                var initial = await characteristic.ReadValueAsync(new Dictionary<string, object>()).WaitAsync(operationTimeout, ct);
                PublishField(aggregate, field.Key, initial, DateTime.UtcNow);
            }

            while (await keepAliveTimer.WaitForNextTickAsync(ct))
            {
                await SendPublicKeepAliveAsync(session, ct);
            }
        }
        finally
        {
            _isConnected = 0;
            _currentSample = null;
            foreach (var watcher in watchers)
                watcher.Dispose();

            foreach (var characteristic in startedNotifications)
            {
                try
                {
                    await characteristic.StopNotifyAsync().WaitAsync(operationTimeout);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "StopNotifyAsync failed during SmartShunt session cleanup");
                }
            }
        }
    }

    private void PublishField(SmartShuntPublicAggregate aggregate, string fieldKey, byte[] value, DateTime updatedAtUtc)
    {
        lock (_stateLock)
            _currentSample = aggregate.Update(fieldKey, value, updatedAtUtc);
    }

    private static async Task SendPublicKeepAliveAsync(DeviceSession session, CancellationToken ct)
    {
        var characteristic = await session.GetCharacteristicAsync(SmartShuntPublicProtocol.PublicKeepAliveUuid, ct);
        ct.ThrowIfCancellationRequested();
        await characteristic.WriteValueAsync(SmartShuntPublicProtocol.PublicKeepAlivePayload, new Dictionary<string, object>())
            .WaitAsync(TimeSpan.FromSeconds(20), ct);
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
                        await _device.DisconnectAsync().WaitAsync(connectTimeout, ct);
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
            var services = await Device.GetServicesAsync().WaitAsync(connectTimeout, ct) ?? [];
            foreach (var service in services)
            {
                var characteristics = await service.GetCharacteristicsAsync().WaitAsync(connectTimeout, ct) ?? [];
                foreach (var characteristic in characteristics)
                {
                    var uuid = await characteristic.GetUUIDAsync().WaitAsync(connectTimeout, ct);
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
                await _device.DisconnectAsync().WaitAsync(connectTimeout);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "DisconnectAsync failed during SmartShunt DeviceSession disposal for {Address}", Address);
            }

            _device = null;
        }

        private async Task<Device> FindDeviceAsync(Adapter adapter, string address, CancellationToken ct)
        {
            var known = await adapter.GetDevicesAsync().WaitAsync(connectTimeout, ct);
            foreach (var device in known)
            {
                if (string.Equals(await device.GetAddressAsync().WaitAsync(connectTimeout, ct), address, StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            var tcs = new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task OnDeviceFound(Adapter sender, DeviceFoundEventArgs eventArgs)
            {
                try
                {
                    if (string.Equals(await eventArgs.Device.GetAddressAsync().WaitAsync(connectTimeout, ct), address, StringComparison.OrdinalIgnoreCase))
                        tcs.TrySetResult(eventArgs.Device);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            }

            adapter.DeviceFound += OnDeviceFound;
            try
            {
                await adapter.StartDiscoveryAsync().WaitAsync(connectTimeout, ct);
                using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                scanCts.CancelAfter(ScanTimeout);
                return await tcs.Task.WaitAsync(scanCts.Token);
            }
            finally
            {
                adapter.DeviceFound -= OnDeviceFound;
                try
                {
                    await adapter.StopDiscoveryAsync().WaitAsync(connectTimeout);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "SmartShunt StopDiscoveryAsync failed during public-session cleanup for {Address}", address);
                }
            }
        }
    }
}

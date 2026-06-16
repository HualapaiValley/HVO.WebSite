using HVO.Hardware.VictronSmartShunt.Configuration;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public sealed class SmartShuntPrivateInfoSource(
    IOptions<SmartShuntOptions> optionsAccessor,
    ILogger<SmartShuntPrivateInfoSource> logger) : ISmartShuntPrivateInfoSource
{
    private static readonly string VictronStreamAUuid = "306b0002-b081-4037-83dc-e59fcc3cdfd0";
    private static readonly string VictronStreamBUuid = "306b0003-b081-4037-83dc-e59fcc3cdfd0";
    private static readonly string VictronStreamCUuid = "306b0004-b081-4037-83dc-e59fcc3cdfd0";
    private static readonly string[] VictronInitSequence306b0002 = ["fa80ff", "f980"];
    private static readonly string[] VictronInitSequence306b0003 = ["01", "0300", "060082189342102703010303"];
    private static readonly (string Uuid, string Hex)[] VictronRichInitSequence =
    [
        (VictronStreamBUuid, "05008119010905008119010a05008119ec0f0500"),
        (VictronStreamBUuid, "050381190100"),
        (VictronStreamCUuid, "8119ec0e05008119010c050081189005008119ec"),
        (VictronStreamBUuid, "3f05008119ec12"),
        (VictronStreamCUuid, "0501811901000501811901000501811901000501"),
        (VictronStreamCUuid, "8119010905018119010a05038119010905038119"),
        (VictronStreamCUuid, "010a05038119010205038119ec0f05038119ec0e"),
        (VictronStreamCUuid, "050381190110050381190fff0503811818050381"),
        (VictronStreamCUuid, "19ed8d05038119ed8f05038119ed8c05038119ee"),
        (VictronStreamCUuid, "ff05038119ed7d050381190383050381190ffe05"),
        (VictronStreamCUuid, "038119034e05038119edec050381190300050381"),
        (VictronStreamBUuid, "190301050381190302050381190305"),
        (VictronStreamBUuid, "81190361050381190362050381191000"),
        (VictronStreamBUuid, "8119eef605038119eef805038119eefb"),
        (VictronStreamBUuid, "05008119ec20"),
        (VictronStreamBUuid, "05008119ec17"),
        (VictronStreamBUuid, "05038119ec5a05038119ec87"),
        (VictronStreamCUuid, "05038119030605038119030705038119030e0503"),
        (VictronStreamCUuid, "8119030f05038119031005038119031105038119"),
        (VictronStreamCUuid, "030a05038119030b050381190303050381190308"),
        (VictronStreamCUuid, "050381190309050381190304"),
        (VictronStreamBUuid, "81190325050381190326050381190327"),
        (VictronStreamCUuid, "050381190320050381190321050381190322050381190323"),
        (VictronStreamCUuid, "050381190324050381190325050381190326050381190327"),
        (VictronStreamCUuid, "050381190328050381190329"),
        (VictronStreamBUuid, "060382191000426300"),
    ];

    private readonly SmartShuntOptions _options = optionsAccessor.Value;
    private readonly ILogger<SmartShuntPrivateInfoSource> _logger = logger;

    public async Task<SmartShuntDeviceInfo?> TryReadAsync(CancellationToken ct)
    {
        if (!_options.EnablePrivateEnrichment)
            return null;

        var adapter = await BlueZManager.GetAdapterAsync(_options.Adapter);
        await using var session = new PrivateSession(_options.Address, TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds), _logger);
        await session.ConnectAsync(adapter, ct);

        var info = new SmartShuntPrivateFrameDecoder();
        var streamA = await session.GetCharacteristicAsync(VictronStreamAUuid, ct);
        var streamB = await session.GetCharacteristicAsync(VictronStreamBUuid, ct);
        var streamC = await session.GetCharacteristicAsync(VictronStreamCUuid, ct);

        using var watcherB = await streamB.WatchPropertiesAsync(changes =>
        {
            foreach (var pair in changes.Changed)
            {
                if (pair.Key == "Value" && pair.Value is byte[] value)
                    info.Observe(value);
            }
        });

        using var watcherC = await streamC.WatchPropertiesAsync(changes =>
        {
            foreach (var pair in changes.Changed)
            {
                if (pair.Key == "Value" && pair.Value is byte[] value)
                    info.Observe(value);
            }
        });

        await StartNotifyIfPossibleAsync(streamB);
        await StartNotifyIfPossibleAsync(streamC);

        foreach (var hex in VictronInitSequence306b0002)
            await WriteHexAsync(streamA, hex);

        foreach (var hex in VictronInitSequence306b0003)
            await WriteHexAsync(streamB, hex);

        foreach (var packet in VictronRichInitSequence)
        {
            var characteristic = string.Equals(packet.Uuid, VictronStreamBUuid, StringComparison.OrdinalIgnoreCase)
                ? streamB
                : streamC;
            await WriteHexAsync(characteristic, packet.Hex);
        }

        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        var result = info.Build();
        if (result is not null)
            _logger.LogInformation("SmartShunt private enrichment updated. Firmware={Firmware} Serial={Serial}", result.FirmwareVersion, result.SerialNumber);

        return result;
    }

    private async Task StartNotifyIfPossibleAsync(IGattCharacteristic1 characteristic)
    {
        try
        {
            await characteristic.StartNotifyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "StartNotifyAsync failed for SmartShunt private characteristic");
        }
    }

    private static async Task WriteHexAsync(IGattCharacteristic1 characteristic, string hex)
    {
        await characteristic.WriteValueAsync(Convert.FromHexString(hex), new Dictionary<string, object>());
    }

    private sealed class PrivateSession(string address, TimeSpan connectTimeout, ILogger logger) : IAsyncDisposable
    {
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(2);
        private Device? _device;
        private readonly ILogger _logger = logger;

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
                    await _device.ConnectAsync().WaitAsync(connectTimeout, ct);
                    await _device.WaitForPropertyValueAsync("Connected", true, connectTimeout);
                    await _device.WaitForPropertyValueAsync("ServicesResolved", true, connectTimeout);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    try
                    {
                        await _device.DisconnectAsync();
                    }
                    catch (Exception disconnectEx)
                    {
                        _logger.LogDebug(disconnectEx, "DisconnectAsync failed during SmartShunt private connect retry for {Address}", Address);
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
                _logger.LogDebug(ex, "DisconnectAsync failed during SmartShunt PrivateSession disposal for {Address}", Address);
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
                    _logger.LogDebug(ex, "SmartShunt StopDiscoveryAsync failed during private-session cleanup for {Address}", address);
                }
            }
        }
    }
}

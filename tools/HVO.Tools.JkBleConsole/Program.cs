using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Tmds.DBus;

var options = Options.Parse(args);

try
{
    return await JkBleConsole.RunAsync(options);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 64;
}

internal static class JkBleConsole
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(10);

    private static readonly string ServiceUuid = BlueZManager.NormalizeUUID("FFE0");
    private static readonly string NotifyUuid = BlueZManager.NormalizeUUID("FFE1");

    private static readonly byte[] CellInfoCommand =
    [
        0xAA, 0x55, 0x90, 0xEB, 0x96, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x10,
    ];

    public static async Task<int> RunAsync(Options options)
    {
        var adapter = await BlueZManager.GetAdapterAsync(options.Adapter);
        Console.WriteLine($"adapter={options.Adapter}");
        Console.WriteLine($"addresses={string.Join(",", options.Addresses)}");
        Console.WriteLine($"rounds={options.Rounds}");
        Console.WriteLine($"interval_seconds={options.IntervalSeconds}");

        var sessions = options.Addresses.Select(address => new DeviceSession(address)).ToList();

        foreach (var session in sessions)
        {
            try
            {
                await session.ConnectAsync(adapter);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"connect_failed address={session.Address} error={Flatten(ex)}");
            }
        }

        var connectedSessions = sessions.Where(s => s.IsReady).ToList();
        Console.WriteLine($"connected_count={connectedSessions.Count}");

        for (var round = 1; round <= options.Rounds; round++)
        {
            Console.WriteLine($"round_start={round}");

            foreach (var session in connectedSessions)
            {
                try
                {
                    var frame = await session.PollAsync(round);
                    Console.WriteLine(
                        $"poll_ok round={round} address={session.Address} chunks={frame.ChunkCount} type=0x{frame.Frame[4]:x2} crc=0x{frame.Frame[^1]:x2}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"poll_failed round={round} address={session.Address} error={Flatten(ex)}");
                }
            }

            Console.WriteLine($"round_end={round}");

            if (round < options.Rounds && options.IntervalSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds));
        }

        foreach (var session in sessions)
            await session.DisposeAsync();

        return connectedSessions.Count == options.Addresses.Count ? 0 : 1;
    }

    private static string Flatten(Exception ex) => ex.GetBaseException().Message.Replace('\n', ' ');

    private sealed class DeviceSession(string address) : IAsyncDisposable
    {
        private readonly object _sync = new();
        private PollRequest? _currentPoll;
        private Device? _device;
        private IGattCharacteristic1? _notifyCharacteristic;
        private IDisposable? _notifyWatcher;

        public string Address { get; } = address;
        public bool IsReady => _device is not null && _notifyCharacteristic is not null;

        public async Task ConnectAsync(Adapter adapter)
        {
            _device = await FindDeviceAsync(adapter, Address);
            Console.WriteLine($"device={await _device.GetAliasAsync()} ({await _device.GetAddressAsync()})");

            await _device.ConnectAsync().WaitAsync(ConnectTimeout);
            await _device.WaitForPropertyValueAsync("Connected", true, ConnectTimeout);
            await _device.WaitForPropertyValueAsync("ServicesResolved", true, ConnectTimeout);

            var service = await _device.GetServiceAsync(ServiceUuid)
                ?? throw new InvalidOperationException($"Service {ServiceUuid} not found.");
            var characteristics = await GattExtensions.GetCharacteristicsAsync(service);
            _notifyCharacteristic = await FindCharacteristicAsync(characteristics, NotifyUuid)
                ?? throw new InvalidOperationException($"Characteristic {NotifyUuid} not found.");

            Console.WriteLine($"service={service.ObjectPath} address={Address}");
            Console.WriteLine($"characteristic={_notifyCharacteristic.ObjectPath} address={Address}");

            _notifyWatcher = await _notifyCharacteristic.WatchPropertiesAsync(OnPropertyChanged);
            await _notifyCharacteristic.StartNotifyAsync();

            Console.WriteLine($"connected address={Address}");
        }

        public async Task<PollResult> PollAsync(int round)
        {
            if (_notifyCharacteristic is null)
                throw new InvalidOperationException("Session is not connected.");

            var request = new PollRequest();
            lock (_sync)
            {
                if (_currentPoll is not null)
                    throw new InvalidOperationException("Poll already in progress.");
                _currentPoll = request;
            }

            try
            {
                Console.WriteLine($"poll_start round={round} address={Address}");
                await _notifyCharacteristic.WriteValueAsync(CellInfoCommand, new Dictionary<string, object>());
                using var notificationCts = new CancellationTokenSource(NotificationTimeout);
                var frame = await request.FrameTcs.Task.WaitAsync(notificationCts.Token);
                return new PollResult(frame, request.Chunks.Count);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_currentPoll, request))
                        _currentPoll = null;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_notifyWatcher is not null)
            {
                _notifyWatcher.Dispose();
                _notifyWatcher = null;
            }

            if (_notifyCharacteristic is not null)
            {
                try
                {
                    await _notifyCharacteristic.StopNotifyAsync();
                }
                catch
                {
                }

                _notifyCharacteristic = null;
            }

            if (_device is not null)
            {
                try
                {
                    await _device.DisconnectAsync();
                }
                catch
                {
                }

                _device = null;
            }
        }

        private void OnPropertyChanged(PropertyChanges changes)
        {
            PollRequest? request;
            lock (_sync)
                request = _currentPoll;

            if (request is null)
                return;

            byte[]? value = null;
            foreach (var pair in changes.Changed)
            {
                if (pair.Key == "Value")
                {
                    value = pair.Value as byte[];
                    break;
                }
            }

            if (value is null)
                return;

            request.Chunks.Add(value);
            Console.WriteLine($"notify len={value.Length} address={Address} hex={Convert.ToHexString(value).ToLowerInvariant()}");

            if (TryAccumulateFrame(request.Buffer, value, out var frame))
                request.FrameTcs.TrySetResult(frame);
        }
    }

    private static async Task<Device> FindDeviceAsync(Adapter adapter, string address)
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
            });

            return Task.CompletedTask;
        }

        adapter.DeviceFound += OnDeviceFound;
        try
        {
            await adapter.StartDiscoveryAsync();
            using var scanCts = new CancellationTokenSource(ScanTimeout);
            return await tcs.Task.WaitAsync(scanCts.Token);
        }
        finally
        {
            adapter.DeviceFound -= OnDeviceFound;
            try
            {
                await adapter.StopDiscoveryAsync();
            }
            catch
            {
            }
        }
    }

    private static async Task<IGattCharacteristic1?> FindCharacteristicAsync(
        IEnumerable<IGattCharacteristic1> characteristics,
        string uuid)
    {
        foreach (var characteristic in characteristics)
        {
            if (string.Equals(await characteristic.GetUUIDAsync(), uuid, StringComparison.OrdinalIgnoreCase))
                return characteristic;
        }

        return null;
    }

    private static bool TryAccumulateFrame(List<byte> buffer, ReadOnlySpan<byte> chunk, out byte[] frame)
    {
        frame = [];

        if (chunk.Length >= 4 && chunk[0] == 0x55 && chunk[1] == 0xAA && chunk[2] == 0xEB && chunk[3] == 0x90)
            buffer.Clear();

        foreach (var b in chunk)
            buffer.Add(b);

        if (buffer.Count < 4)
            return false;

        var sofIndex = FindSof(buffer);
        if (sofIndex < 0)
        {
            if (buffer.Count > 3)
                buffer.RemoveRange(0, buffer.Count - 3);
            return false;
        }

        if (sofIndex > 0)
            buffer.RemoveRange(0, sofIndex);

        if (buffer.Count < 300)
            return false;

        frame = [.. buffer.Take(300)];
        buffer.RemoveRange(0, 300);
        return true;
    }

    private static int FindSof(List<byte> buffer)
    {
        for (var i = 0; i <= buffer.Count - 4; i++)
        {
            if (buffer[i] == 0x55 && buffer[i + 1] == 0xAA && buffer[i + 2] == 0xEB && buffer[i + 3] == 0x90)
                return i;
        }

        return -1;
    }

    private sealed class PollRequest
    {
        public List<byte[]> Chunks { get; } = [];
        public List<byte> Buffer { get; } = [];
        public TaskCompletionSource<byte[]> FrameTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record PollResult(byte[] Frame, int ChunkCount);
}

internal sealed record Options(IReadOnlyList<string> Addresses, string Adapter, int Rounds, int IntervalSeconds)
{
    public static Options Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Usage: HVO.Tools.JkBleConsole <BLE-MAC> [<BLE-MAC> ...] [--adapter hci0] [--rounds 3] [--interval-seconds 5]");

        var addresses = new List<string>();
        var adapter = "hci0";
        var rounds = 3;
        var intervalSeconds = 5;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--adapter":
                    adapter = RequireValue(args, ref i, "--adapter");
                    break;
                case "--rounds":
                    rounds = int.Parse(RequireValue(args, ref i, "--rounds"));
                    break;
                case "--interval-seconds":
                    intervalSeconds = int.Parse(RequireValue(args, ref i, "--interval-seconds"));
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option: {args[i]}");
                    addresses.Add(args[i]);
                    break;
            }
        }

        if (addresses.Count == 0)
            throw new ArgumentException("At least one BLE MAC address is required.");

        return new Options(addresses, adapter, rounds, intervalSeconds);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {option}");
        index++;
        return args[index];
    }
}

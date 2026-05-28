using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using System.Text.Json;
using Tmds.DBus;

var options = Options.Parse(args);

try
{
    return await SmartShuntBleConsole.RunAsync(options);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 64;
}

internal static class SmartShuntBleConsole
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(2);

    private static readonly string PublicKeepAliveUuid = "6597ffff-4bda-4c1e-af4b-551c4cf74769";
    private static readonly string VictronStreamAUuid = "306b0002-b081-4037-83dc-e59fcc3cdfd0";
    private static readonly string VictronStreamBUuid = "306b0003-b081-4037-83dc-e59fcc3cdfd0";
    private static readonly string VictronStreamCUuid = "306b0004-b081-4037-83dc-e59fcc3cdfd0";

    private static readonly string[] VictronInitSequence306b0002 = ["fa80ff", "f980"];
    private static readonly string[] VictronInitSequence306b0003 = ["01", "0300", "060082189342102703010303"];
    private static readonly (string Uuid, string Hex)[] VictronRichInitSequence =
    [
        (VictronStreamBUuid, "05008119010905008119010a05008119ec0f0500"),
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
        (VictronStreamBUuid, "060382191000426300"),
    ];
    private static readonly (string Uuid, string Hex)[] VictronReferenceInitSequence =
    [
        (VictronStreamBUuid, "05008119010905008119010a05008119ec0f0500"),
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
        (VictronStreamBUuid, "81190325050381190326050381190327"),
        (VictronStreamBUuid, "81190361050381190362050381191000"),
        (VictronStreamBUuid, "8119eef605038119eef805038119eefb"),
        (VictronStreamBUuid, "05008119ec20"),
        (VictronStreamBUuid, "05008119ec17"),
        (VictronStreamBUuid, "060382191000426300"),
    ];
    private static readonly (string Uuid, string Hex)[] VictronExtendedInitSequence =
    [
        (VictronStreamCUuid, "05038119030605038119030705038119030e0503"),
        (VictronStreamCUuid, "8119030f05038119031005038119031105038119"),
        (VictronStreamCUuid, "030a05038119030b050381190303050381190308"),
        (VictronStreamCUuid, "0503811903090503811903040503811910300503"),
        (VictronStreamCUuid, "8119031d05038119031e05038119eefc05038119"),
        (VictronStreamCUuid, "0328050381190329050381190320050381190321"),
        (VictronStreamBUuid, "81190325050381190326050381190327"),
        (VictronStreamCUuid, "050381191000050381191001050381191002050381"),
        (VictronStreamCUuid, "191003"),
        (VictronStreamCUuid, "0503811910090503811903500503811903510503"),
        (VictronStreamCUuid, "8119100505038119100405038119100605038119"),
        (VictronStreamCUuid, "100705038119102c050381191029050381190ffd"),
        (VictronStreamCUuid, "05038119100805038119eefe0503811904000503"),
        (VictronStreamCUuid, "8119eef505038119eee005038119eee305038119"),
        (VictronStreamCUuid, "eee805038119eee405038119eee505038119eee6"),
        (VictronStreamCUuid, "05038119eee105038119eee705038119eee20503"),
        (VictronStreamCUuid, "05038119eefa05038119eef705038119eef40503"),
        (VictronStreamCUuid, "8119015005038118900503811891050381040503"),
        (VictronStreamCUuid, "8119103405038119ec3f05038119ec1205008119"),
        (VictronStreamCUuid, "ec1305008119ec1405008119ec1505008119ec16"),
    ];
    private static readonly (string Uuid, string Hex)[] VictronSyncProbeSequence =
    [
        (VictronStreamCUuid, "05038119102c050381191029050381190ffd"),
        (VictronStreamCUuid, "050381190309"),
    ];
    private static readonly (string Uuid, string Hex)[] VictronLateSettingsProbeSequence =
    [
        (VictronStreamCUuid, "050381191000050381191001050381191002050381"),
        (VictronStreamCUuid, "191003050381191004050381191005050381191006"),
        (VictronStreamCUuid, "050381191007050381191008050381191009"),
    ];
    private static readonly string VictronKeepalive306b0002 = "f941";
    private static readonly byte[] PublicKeepAlivePayload = [0x20, 0x4e];

    private static readonly PublicField[] PublicFields =
    [
        new("soc", "65970fff-4bda-4c1e-af4b-551c4cf74769", "State of charge", "%", DecodeUnsignedHundredths, "ffff"),
        new("voltage", "6597ed8d-4bda-4c1e-af4b-551c4cf74769", "Voltage", "V", DecodeSignedHundredths, "ff7f"),
        new("power", "6597ed8e-4bda-4c1e-af4b-551c4cf74769", "Power", "W", DecodeNegatedSignedInt16, "ff7f"),
        new("current", "6597ed8c-4bda-4c1e-af4b-551c4cf74769", "Current", "A", DecodeNegatedSignedThousandths, "ffffff7f"),
        new("consumed_ah", "6597eeff-4bda-4c1e-af4b-551c4cf74769", "Consumed Ah", "Ah", DecodeSignedTenths, "ffffff7f"),
        new("starter_voltage", "6597ed7d-4bda-4c1e-af4b-551c4cf74769", "Starter battery voltage", "V", DecodeSignedHundredths, "ff7f"),
        new("val2", "6597edec-4bda-4c1e-af4b-551c4cf74769", "Value 2", "raw", DecodeUnsignedInt16, "ffff"),
        new("val3", "65970382-4bda-4c1e-af4b-551c4cf74769", "Value 3", "raw", DecodeUnsignedInt16, "ffff"),
        new("temperature", "65970383-4bda-4c1e-af4b-551c4cf74769", "Temperature?", "raw", DecodeSignedInt16, "ff7f"),
        new("remaining_time", "65970ffe-4bda-4c1e-af4b-551c4cf74769", "Remaining time", "min", DecodeUnsignedInt16, "ffff"),
    ];

    public static async Task<int> RunAsync(Options options)
    {
        Console.WriteLine($"command={options.Command}");
        Console.WriteLine($"address={options.Address}");
        Console.WriteLine($"adapter={options.Adapter}");

        var adapter = await BlueZManager.GetAdapterAsync(options.Adapter);
        await using var session = new DeviceSession(options.Address);
        await session.ConnectAsync(adapter);

        switch (options.Command)
        {
            case Command.Services:
                await DumpServicesAsync(session);
                break;
            case Command.Read:
                await ReadLoopAsync(session, options);
                break;
            case Command.Notify:
                await CaptureNotificationsAsync(session, options);
                break;
            case Command.Write:
                await WriteAsync(session, options);
                break;
            case Command.VictronInit:
                await RunVictronInitAsync(session, options);
                break;
            case Command.PrivateMonitor:
                await RunVictronInitAsync(session, options, decodePrivateFrames: true);
                break;
            case Command.PublicSnapshot:
                await RunPublicSnapshotAsync(session);
                break;
            case Command.PublicMonitor:
                await RunPublicMonitorAsync(session, options);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Command), options.Command, null);
        }

        return 0;
    }

    private static async Task DumpServicesAsync(DeviceSession session)
    {
        var services = await session.Device.GetServicesAsync() ?? [];
        if (services.Count == 0)
        {
            Console.WriteLine("services=0");
            return;
        }

        foreach (var service in services)
        {
            var serviceUuid = await service.GetUUIDAsync();
            Console.WriteLine($"service uuid={serviceUuid} path={service.ObjectPath}");

            var characteristics = await service.GetCharacteristicsAsync() ?? [];
            foreach (var characteristic in characteristics)
            {
                var characteristicUuid = await characteristic.GetUUIDAsync();
                var flags = await characteristic.GetFlagsAsync() ?? [];
                Console.WriteLine(
                    $"characteristic uuid={characteristicUuid} flags=[{string.Join(",", flags)}] path={characteristic.ObjectPath}");
            }
        }
    }

    private static async Task ReadLoopAsync(DeviceSession session, Options options)
    {
        var characteristic = await session.GetCharacteristicAsync(options.CharacteristicUuid);
        var count = options.Count ?? 1;
        for (var i = 1; i <= count; i++)
        {
            var value = await characteristic.ReadValueAsync(new Dictionary<string, object>());
            Console.WriteLine($"read index={i} len={value.Length} hex={Convert.ToHexString(value).ToLowerInvariant()}");

            if (i < count && options.IntervalSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds));
        }
    }

    private static async Task CaptureNotificationsAsync(DeviceSession session, Options options)
    {
        var characteristic = await session.GetCharacteristicAsync(options.CharacteristicUuid);
        using var watcher = await characteristic.WatchPropertiesAsync(changes =>
        {
            foreach (var pair in changes.Changed)
            {
                if (pair.Key != "Value" || pair.Value is not byte[] value)
                    continue;

                Console.WriteLine(
                    $"notify ts={DateTimeOffset.UtcNow:O} len={value.Length} hex={Convert.ToHexString(value).ToLowerInvariant()}");
            }
        });

        await characteristic.StartNotifyAsync();
        Console.WriteLine($"notify_started characteristic={options.CharacteristicUuid} duration_seconds={options.DurationSeconds}");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.DurationSeconds));
        }
        finally
        {
            try
            {
                await characteristic.StopNotifyAsync();
            }
            catch
            {
            }
        }
    }

    private static async Task WriteAsync(DeviceSession session, Options options)
    {
        if (string.IsNullOrWhiteSpace(options.HexValue))
            throw new ArgumentException("--hex is required for write.");

        var characteristic = await session.GetCharacteristicAsync(options.CharacteristicUuid);
        var value = ParseHex(options.HexValue);
        LogPrivateWriteIntent(options.CharacteristicUuid, value);
        await characteristic.WriteValueAsync(value, new Dictionary<string, object>());
        Console.WriteLine($"write characteristic={options.CharacteristicUuid} len={value.Length} hex={Convert.ToHexString(value).ToLowerInvariant()}");
    }

    private static async Task RunVictronInitAsync(DeviceSession session, Options options, bool decodePrivateFrames = false)
    {
        var streamA = await session.GetCharacteristicAsync(VictronStreamAUuid);
        var streamB = await session.GetCharacteristicAsync(VictronStreamBUuid);
        var streamC = await session.GetCharacteristicAsync(VictronStreamCUuid);
        var privateSummary = decodePrivateFrames ? new PrivateMonitorSummary() : null;
        using var privateExportWriter = decodePrivateFrames && !string.IsNullOrWhiteSpace(options.ExportPrivateDecodedPath)
            ? new PrivateDecodedExportWriter(options.ExportPrivateDecodedPath)
            : null;

        using var watcherA = await WatchCharacteristicAsync(streamA, VictronStreamAUuid, decodePrivateFrames, privateSummary, privateExportWriter);
        using var watcherB = await WatchCharacteristicAsync(streamB, VictronStreamBUuid, decodePrivateFrames, privateSummary, privateExportWriter);
        using var watcherC = await WatchCharacteristicAsync(streamC, VictronStreamCUuid, decodePrivateFrames, privateSummary, privateExportWriter);

        await StartNotifyIfSupportedAsync(streamA, VictronStreamAUuid);
        await StartNotifyIfSupportedAsync(streamB, VictronStreamBUuid);
        await StartNotifyIfSupportedAsync(streamC, VictronStreamCUuid);

        try
        {
            foreach (var hex in VictronInitSequence306b0002)
                await WriteHexAsync(streamA, VictronStreamAUuid, hex);

            foreach (var hex in VictronInitSequence306b0003)
                await WriteHexAsync(streamB, VictronStreamBUuid, hex);

            if (options.PrivateProfile == PrivateProfile.Rich || options.PrivateProfile == PrivateProfile.Reference || options.PrivateProfile == PrivateProfile.Extended || options.PrivateProfile == PrivateProfile.SyncProbe)
            {
                var initSequence = options.PrivateProfile == PrivateProfile.Reference
                    ? VictronReferenceInitSequence
                    : options.PrivateProfile == PrivateProfile.SyncProbe
                        ? VictronSyncProbeSequence
                        : VictronRichInitSequence;

                foreach (var packet in initSequence)
                {
                    var characteristic = string.Equals(packet.Uuid, VictronStreamBUuid, StringComparison.OrdinalIgnoreCase)
                        ? streamB
                        : streamC;
                    await WriteHexAsync(characteristic, packet.Uuid, packet.Hex);
                }
            }

            if (options.PrivateProfile == PrivateProfile.Extended)
            {
                foreach (var packet in VictronExtendedInitSequence)
                {
                    var characteristic = string.Equals(packet.Uuid, VictronStreamBUuid, StringComparison.OrdinalIgnoreCase)
                        ? streamB
                        : streamC;
                    await WriteHexAsync(characteristic, packet.Uuid, packet.Hex);
                }
            }

            var durationSeconds = options.DurationSeconds <= 0 ? 30 : options.DurationSeconds;
            var keepaliveIntervalSeconds = options.IntervalSeconds <= 0 ? 10 : options.IntervalSeconds;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);
            var lateSettingsProbeSent = false;
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    await WriteHexAsync(streamA, VictronStreamAUuid, VictronKeepalive306b0002);
                }
                catch (Exception ex) when (IsNotConnected(ex))
                {
                    Console.WriteLine($"keepalive_stopped characteristic={VictronStreamAUuid} error={Flatten(ex)}");
                    break;
                }

                if (!lateSettingsProbeSent && options.PrivateProfile == PrivateProfile.Extended)
                {
                    foreach (var packet in VictronLateSettingsProbeSequence)
                    {
                        var characteristic = string.Equals(packet.Uuid, VictronStreamBUuid, StringComparison.OrdinalIgnoreCase)
                            ? streamB
                            : streamC;
                        await WriteHexAsync(characteristic, packet.Uuid, packet.Hex);
                    }

                    lateSettingsProbeSent = true;
                }

                await Task.Delay(TimeSpan.FromSeconds(keepaliveIntervalSeconds));
            }

            privateSummary?.WriteToConsole();
        }
        finally
        {
            await StopNotifyQuietlyAsync(streamA);
            await StopNotifyQuietlyAsync(streamB);
            await StopNotifyQuietlyAsync(streamC);
        }
    }

    private static async Task RunPublicSnapshotAsync(DeviceSession session)
    {
        await SendPublicKeepAliveAsync(session);

        foreach (var field in PublicFields)
        {
            var characteristic = await session.GetCharacteristicAsync(field.Uuid);
            var value = await characteristic.ReadValueAsync(new Dictionary<string, object>());
            Console.WriteLine(FormatPublicField(field, value));
        }
    }

    private static async Task RunPublicMonitorAsync(DeviceSession session, Options options)
    {
        var durationSeconds = options.DurationSeconds <= 0 ? 30 : options.DurationSeconds;
        var keepAliveIntervalSeconds = options.IntervalSeconds <= 0 ? 10 : options.IntervalSeconds;
        var currentState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var startedNotifications = new List<IGattCharacteristic1>();
        var watchers = new List<IDisposable>();

        try
        {
            await SendPublicKeepAliveAsync(session);

            foreach (var field in PublicFields)
            {
                var characteristic = await session.GetCharacteristicAsync(field.Uuid);
                watchers.Add(await characteristic.WatchPropertiesAsync(changes =>
                {
                    foreach (var pair in changes.Changed)
                    {
                        if (pair.Key != "Value" || pair.Value is not byte[] value)
                            continue;

                        var formatted = FormatPublicField(field, value);
                        currentState[field.Key] = formatted;
                        Console.WriteLine($"notify {formatted}");
                    }
                }));

                try
                {
                    await characteristic.StartNotifyAsync();
                    startedNotifications.Add(characteristic);
                    Console.WriteLine($"notify_started characteristic={field.Uuid}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"notify_start_failed characteristic={field.Uuid} error={Flatten(ex)}");
                }

                var initialValue = await characteristic.ReadValueAsync(new Dictionary<string, object>());
                var formattedInitial = FormatPublicField(field, initialValue);
                currentState[field.Key] = formattedInitial;
                Console.WriteLine($"initial {formattedInitial}");
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(keepAliveIntervalSeconds));
                await SendPublicKeepAliveAsync(session);
                Console.WriteLine($"state ts={DateTimeOffset.UtcNow:O}");
                foreach (var field in PublicFields)
                {
                    if (currentState.TryGetValue(field.Key, out var value) && !string.IsNullOrWhiteSpace(value))
                        Console.WriteLine(value);
                }
            }
        }
        finally
        {
            foreach (var watcher in watchers)
                watcher.Dispose();

            foreach (var characteristic in startedNotifications)
                await StopNotifyQuietlyAsync(characteristic);
        }
    }

    private static async Task<IDisposable> WatchCharacteristicAsync(
        IGattCharacteristic1 characteristic,
        string uuid,
        bool decodePrivateFrames = false,
        PrivateMonitorSummary? privateSummary = null,
        PrivateDecodedExportWriter? privateExportWriter = null)
    {
        return await characteristic.WatchPropertiesAsync(changes =>
        {
            foreach (var pair in changes.Changed)
            {
                if (pair.Key != "Value" || pair.Value is not byte[] value)
                    continue;

                var timestamp = DateTimeOffset.UtcNow;
                var hex = Convert.ToHexString(value).ToLowerInvariant();
                Console.WriteLine(
                    $"notify characteristic={uuid} ts={timestamp:O} len={value.Length} hex={hex}");

                if (decodePrivateFrames)
                {
                    var decodedLines = DecodePrivateFrame(uuid, value).ToArray();
                    privateSummary?.Observe(decodedLines);
                    privateExportWriter?.Write(timestamp, uuid, decodedLines);
                    foreach (var decoded in decodedLines)
                        Console.WriteLine(decoded);
                }
            }
        });
    }

    private static IEnumerable<string> DecodePrivateFrame(string uuid, byte[] value)
    {
        if (string.Equals(uuid, VictronStreamAUuid, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"private stream=306b0002 kind={DecodeStreamAKind(value)}";
            yield break;
        }

        if (!string.Equals(uuid, VictronStreamBUuid, StringComparison.OrdinalIgnoreCase))
            yield break;

        foreach (var entry in TryDecodeRegisterEntries(value))
            yield return entry;
    }

    private static string DecodeStreamAKind(byte[] value)
    {
        var hex = Convert.ToHexString(value).ToLowerInvariant();
        return hex switch
        {
            "f901" => "ack",
            "00040001de4a00" => "status?",
            _ => $"raw hex={hex}",
        };
    }

    private static IEnumerable<string> TryDecodeRegisterEntries(byte[] value)
    {
        var recognized = false;
        var offset = 0;
        while (offset < value.Length)
        {
            var consumed = TryDecodePrivateEntry(value.AsSpan(offset), out var decoded);
            if (consumed <= 0)
            {
                if (!recognized)
                    yield return $"private stream=306b0003 undecoded hex={Convert.ToHexString(value).ToLowerInvariant()}";
                else
                    yield return $"private stream=306b0003 trailing hex={Convert.ToHexString(value.AsSpan(offset).ToArray()).ToLowerInvariant()}";

                yield break;
            }

            recognized = true;
            yield return decoded;
            offset += consumed;
        }
    }

    private static int TryDecodePrivateEntry(ReadOnlySpan<byte> value, out string decoded)
    {
        decoded = string.Empty;
        if (value.Length < 6)
            return -1;

        if (value[1] != 0x03 && value[1] != 0x01 && value[1] != 0x00)
            return -1;

        if (value[2] != 0x19)
            return -1;

        if (value[0] == 0x08)
            return TryDecodeVariableLengthPrivateEntry(value, out decoded);

        if (value[0] == 0x09)
            return TryDecodeFixedLengthPrivateEntry(value, out decoded);

        return -1;
    }

    private static int TryDecodeVariableLengthPrivateEntry(ReadOnlySpan<byte> value, out string decoded)
    {
        decoded = string.Empty;
        if (value.Length < 6)
            return -1;

        var categoryPrefix = value[1];
        var category = value[3];
        var command = value[4];
        var lengthType = value[5];
        var payloadLength = lengthType & 0x0f;

        if (payloadLength < 0 || value.Length < 6 + payloadLength)
            return -1;

        var payload = value.Slice(6, payloadLength).ToArray();
        decoded = FormatPrivateCategoryValue(categoryPrefix, category, command, lengthType, payload);
        return 6 + payloadLength;
    }

    private static int TryDecodeFixedLengthPrivateEntry(ReadOnlySpan<byte> value, out string decoded)
    {
        decoded = string.Empty;
        if (value.Length < 6)
            return -1;

        var categoryPrefix = value[1];
        var category = value[3];
        var command = value[4];
        var payload = value.Slice(5, 1).ToArray();
        decoded = FormatPrivateCategoryValue(categoryPrefix, category, command, 0x01, payload);
        return 6;
    }

    private static string FormatPrivateCategoryValue(byte categoryPrefix, byte category, byte command, byte lengthType, byte[] payload)
    {
        var categoryKey = (categoryPrefix, category) switch
        {
            (0x03, 0xed) => "latest",
            (0x03, 0xee) => "latest_ext",
            (0x03, 0x03) => "history",
            (0x03, 0x10) => "settings",
            (0x03, 0x0f) => "mixed_settings",
            (0x03, 0x01) => "product",
            (0x03, 0xec) => "streaming",
            (0x00, 0x01) => "product",
            (0x00, 0xec) => "streaming",
            (0x01, 0x01) => "product",
            (0x01, 0xec) => "streaming",
            _ => $"category_{categoryPrefix:x2}_{category:x2}",
        };

        var (name, valueText) = categoryKey switch
        {
            "latest" => FormatPrivateMappedValue(categoryKey, command, payload),
            "latest_ext" => FormatPrivateMappedValue(categoryKey, command, payload),
            "history" => FormatPrivateMappedValue(categoryKey, command, payload),
            "settings" => FormatPrivateMappedValue(categoryKey, command, payload),
            "mixed_settings" => FormatPrivateMappedValue(categoryKey, command, payload),
            "product" => FormatPrivateMappedValue(categoryKey, command, payload),
            "streaming" => FormatPrivateMappedValue(categoryKey, command, payload),
            _ => ("unknown", FormatRawPrivatePayload(payload)),
        };

        return $"private stream=306b0003 category={categoryKey} command=0x{command:x2} name={name} len={payload.Length} type=0x{lengthType:x2} hex={Convert.ToHexString(payload).ToLowerInvariant()} value={valueText}";
    }

    private static (string Name, string Value) FormatPrivateMappedValue(string categoryKey, byte command, byte[] payload)
    {
        return categoryKey switch
        {
            "latest" => command switch
            {
                0x8c => ("Current", (-BitConverter.ToInt32(payload, 0) / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)),
                0x8d => ("Voltage", (BitConverter.ToInt16(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x8e => ("Power", (-BitConverter.ToInt16(payload, 0)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x8f => ("Current (coarse)", (BitConverter.ToInt16(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                0x7d => ("Starter", MatchesNotAvailable(payload, "ff7f") ? "n/a" : (BitConverter.ToInt16(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0xec => ("Value 2", MatchesNotAvailable(payload, "ffff") ? "n/a" : BitConverter.ToUInt16(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0xff => ("Consumed Ah", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                _ => ($"latest_0x{command:x2}", FormatRawPrivatePayload(payload)),
            },
            "latest_ext" => command switch
            {
                0xff => ("Consumed Ah", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                _ => ($"latest_ext_0x{command:x2}", FormatRawPrivatePayload(payload)),
            },
            "history" => command switch
            {
                0x00 => ("Deepest Discharge", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                0x01 => ("Last Discharge", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                0x02 => ("Average Discharge", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                0x03 => ("Total Charge Cycles", BitConverter.ToUInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x04 => ("Full Discharges", BitConverter.ToUInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x05 => ("Cumulative Ah Drawn", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                0x06 => ("Min Battery Voltage", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x07 => ("Max Battery Voltage", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x08 => ("Time Since Last Full", BitConverter.ToInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x09 => ("Synchronizations", BitConverter.ToUInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x0a => ("Low Voltage Alarms", BitConverter.ToUInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x0b => ("High Voltage Alarms", BitConverter.ToUInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x0e => ("Min Starter Voltage", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x0f => ("Max Starter Voltage", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x10 => ("Discharged Energy", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x83 => ("Temperature?", MatchesNotAvailable(payload, "ff7f") ? "n/a" : BitConverter.ToInt16(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x11 => ("Charged Energy", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x20 => ("Alarm Low Voltage Set", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x21 => ("Alarm Low Voltage Clear", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x22 => ("Alarm High Voltage Set", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x23 => ("Alarm High Voltage Clear", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x24 => ("Alarm Low Starter Set", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x25 => ("Alarm Low Starter Clear", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x26 => ("Alarm High Starter Set", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x27 => ("Alarm High Starter Clear", (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                0x28 => ("Alarm Low SOC Set", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                0x29 => ("Alarm Low SOC Clear", (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                _ => ($"history_0x{command:x2}", FormatRawPrivatePayload(payload)),
            },
            "settings" => command switch
            {
                0x00 => ("Capacity", FormatUnsignedPrivateValue(payload, 1, 0)),
                0x01 => ("Charged Voltage", FormatUnsignedPrivateValue(payload, 10.0, 1)),
                0x02 => ("Tail Current", FormatUnsignedPrivateValue(payload, 10.0, 1)),
                0x03 => ("Charged Detection Time", FormatUnsignedPrivateValue(payload, 1, 0)),
                0x04 => ("Charge Efficiency Factor", FormatUnsignedPrivateValue(payload, 1, 0)),
                0x05 => ("Peukert Coefficient", FormatUnsignedPrivateValue(payload, 100.0, 2)),
                0x06 => ("Current Threshold", FormatUnsignedPrivateValue(payload, 100.0, 2)),
                0x07 => ("Time-To-Go Average Period", FormatUnsignedPrivateValue(payload, 1, 0)),
                0x08 => ("Discharge Floor", FormatUnsignedPrivateValue(payload, 10.0, 1)),
                0x09 => ("Relay Low SOC Clear", FormatUnsignedPrivateValue(payload, 1, 0)),
                0x31 => ("Streaming Data Setting", FormatRawPrivatePayload(payload)),
                0x50 => ("History Details", FormatRawPrivatePayload(payload)),
                _ => ($"settings_0x{command:x2}", FormatRawPrivatePayload(payload)),
            },
            "mixed_settings" => command switch
            {
                0xfe => ("Time to go", MatchesNotAvailable(payload, "ffff") ? "n/a" : BitConverter.ToInt16(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0xff => ("Charge Status", (BitConverter.ToUInt16(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                _ => ($"mixed_0x{command:x2}", FormatRawPrivatePayload(payload)),
            },
            "product" => command switch
            {
                0x00 => ("Product ID", FormatRawPrivatePayload(payload)),
                0x02 => ("Firmware Version", DecodeFirmware(payload)),
                0x09 => ("Device Id?", FormatRawPrivatePayload(payload)),
                0x0a => ("Serial", DecodeAscii(payload)),
                0x10 => ("Udf Version", DecodeUdf(payload)),
                0x50 => ("Product Metadata 0x50", BitConverter.ToInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                _ => ($"product_0x{command:x2}", FormatRawPrivatePayload(payload)),
            },
            "streaming" => command switch
            {
                0x0e => ("Streaming Bool 0x0e", payload[0].ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x0f => ("Streaming Bool 0x0f", payload[0].ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x5a => ("Streaming Counter", BitConverter.ToUInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                0x87 => ("Charge Status (coarse)", payload[0].ToString(System.Globalization.CultureInfo.InvariantCulture)),
                _ => ($"streaming_0x{command:x2}", FormatRawPrivatePayload(payload)),
            },
            _ => ($"{categoryKey}_0x{command:x2}", FormatRawPrivatePayload(payload)),
        };
    }

    private static string DecodeFirmware(byte[] payload)
    {
        if (payload.Length < 4)
            return FormatRawPrivatePayload(payload);

        var version = payload.AsSpan(1, 3).ToArray();
        if (version[2] == 0xff && version[1] == 0xff && version[0] == 0xff)
            return "NO FIRMWARE";

        return version[2] != 0
            ? $"v{version[2]}{version[1]:00}.{version[0]:00}"
            : $"v{version[1]}.{version[0]:00}";
    }

    private static string DecodeUdf(byte[] payload)
    {
        if (payload.Length < 3)
            return FormatRawPrivatePayload(payload);

        var version = payload.AsSpan(0, Math.Min(3, payload.Length)).ToArray();
        if (version.Length == 3 && version[2] == 0xff && version[1] == 0xff && version[0] == 0xff)
            return "NO FIRMWARE";

        return version.Length >= 3 && version[2] != 0
            ? $"v{version[2]}{version[1]:00}.{version[0]:00}"
            : $"v{version[1]}.{version[0]:00}";
    }

    private static string DecodeAscii(byte[] payload)
    {
        try
        {
            return System.Text.Encoding.ASCII.GetString(payload);
        }
        catch
        {
            return FormatRawPrivatePayload(payload);
        }
    }

    private static string FormatRawPrivatePayload(byte[] payload)
        => Convert.ToHexString(payload).ToLowerInvariant();

    private static string FormatUnsignedPrivateValue(byte[] payload, double divisor, int decimals)
    {
        if (!TryReadUnsignedPayload(payload, out var value))
            return FormatRawPrivatePayload(payload);

        if (Math.Abs(divisor - 1) < double.Epsilon)
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return (value / divisor).ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryReadUnsignedPayload(byte[] payload, out ulong value)
    {
        value = payload.Length switch
        {
            1 => payload[0],
            2 => BitConverter.ToUInt16(payload, 0),
            4 => BitConverter.ToUInt32(payload, 0),
            8 => BitConverter.ToUInt64(payload, 0),
            _ => 0,
        };

        return payload.Length is 1 or 2 or 4 or 8;
    }

    private static string FormatPrivateRegister(ushort register, byte type, byte[] payload)
    {
        var key = $"0x{register:x4}";
        var name = register switch
        {
            0xed8c => "current?",
            0xed8d => "voltage?",
            0xed8e => "power?",
            0xed8f => "private-live?",
            0xeeff => "consumed_ah?",
            0xec5a => "counter/state?",
            0x0308 => "counter/state?",
            0x0311 => "history/stat?",
            0x1893 => "session/request?",
            _ => "unknown",
        };

        var hex = Convert.ToHexString(payload).ToLowerInvariant();
        var valueText = register switch
        {
            0xed8c when payload.Length == 4 => (-BitConverter.ToInt32(payload, 0) / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            0xed8d when payload.Length == 2 => (BitConverter.ToInt16(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            0xed8e when payload.Length == 2 => (-BitConverter.ToInt16(payload, 0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            0xed8f when payload.Length == 2 => BitConverter.ToUInt16(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            0xeeff when payload.Length == 4 => (BitConverter.ToInt32(payload, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
            0x0311 when payload.Length == 4 => (BitConverter.ToInt32(payload, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            0x0308 when payload.Length == 4 => BitConverter.ToInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            0xec5a when payload.Length == 4 => BitConverter.ToInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ when type == 0x42 && payload.Length == 2 => BitConverter.ToInt16(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ when type == 0x44 && payload.Length == 4 => BitConverter.ToInt32(payload, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "n/a",
        };

        return $"private stream=306b0003 register={key} name={name} type=0x{type:x2} hex={hex} value={valueText}";
    }

    private static async Task StartNotifyIfSupportedAsync(IGattCharacteristic1 characteristic, string uuid)
    {
        try
        {
            await characteristic.StartNotifyAsync();
            Console.WriteLine($"notify_started characteristic={uuid}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"notify_start_failed characteristic={uuid} error={Flatten(ex)}");
        }
    }

    private static async Task StopNotifyQuietlyAsync(IGattCharacteristic1 characteristic)
    {
        try
        {
            await characteristic.StopNotifyAsync();
        }
        catch
        {
        }
    }

    private static async Task WriteHexAsync(IGattCharacteristic1 characteristic, string uuid, string hex)
    {
        var value = ParseHex(hex);
        LogPrivateWriteIntent(uuid, value);
        await characteristic.WriteValueAsync(value, new Dictionary<string, object>());
        Console.WriteLine($"write characteristic={uuid} len={value.Length} hex={hex.ToLowerInvariant()}");
    }

    private static void LogPrivateWriteIntent(string uuid, byte[] value)
    {
        var description = DescribePrivateWriteIntent(uuid, value);
        if (!string.IsNullOrWhiteSpace(description))
            Console.WriteLine(description);
    }

    private static string? DescribePrivateWriteIntent(string uuid, byte[] value)
    {
        if (!string.Equals(uuid, VictronStreamBUuid, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uuid, VictronStreamCUuid, StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = ScanPrivateWriteSegments(value).ToArray();
        if (segments.Length == 0)
        {
            return $"write_intent characteristic={uuid} transport=private-victron frame=unknown hex={Convert.ToHexString(value).ToLowerInvariant()}";
        }

        var frame = segments.All(segment => segment.Kind == "read-like")
            ? "read-like"
            : segments.All(segment => segment.Kind == "write-like")
                ? "write-like"
                : "mixed";

        var targets = string.Join(",", segments.Select(FormatPrivateWriteSegment));
        var note = frame switch
        {
            "read-like" => "selector/query frame, not a confirmed state-changing write",
            "write-like" => "host-to-device write frame; tail bytes may include CBOR payload",
            _ => "mixed frame; inspect offsets before treating as a control path",
        };

        return $"write_intent characteristic={uuid} transport=private-victron frame={frame} targets=[{targets}] note={note}";
    }

    private static IReadOnlyList<PrivateWriteSegment> ScanPrivateWriteSegments(byte[] value)
    {
        var segments = new List<PrivateWriteSegment>();
        for (var offset = 0; offset < value.Length; offset++)
        {
            if (value.Length - offset >= 6
                && value[offset] == 0x05
                && value[offset + 2] == 0x81
                && value[offset + 3] == 0x19)
            {
                segments.Add(new PrivateWriteSegment(
                    offset,
                    "read-like",
                    $"selector=0x{value[offset + 1]:x2}",
                    (ushort)((value[offset + 4] << 8) | value[offset + 5]),
                    null));
                continue;
            }

            if (value.Length - offset >= 6
                && value[offset] == 0x06
                && value[offset + 2] == 0x82
                && value[offset + 3] == 0x19)
            {
                segments.Add(new PrivateWriteSegment(
                    offset,
                    "write-like",
                    $"selector=0x{value[offset + 1]:x2}",
                    (ushort)((value[offset + 4] << 8) | value[offset + 5]),
                    value.Length - offset > 6 ? Convert.ToHexString(value, offset + 6, value.Length - (offset + 6)).ToLowerInvariant() : null));
                continue;
            }

            if (value.Length - offset >= 5
                && value[offset] == 0x06
                && value[offset + 2] == 0x82
                && value[offset + 3] == 0x18)
            {
                segments.Add(new PrivateWriteSegment(
                    offset,
                    "write-like",
                    $"selector=0x{value[offset + 1]:x2}",
                    null,
                    $"command=0x{value[offset + 4]:x2}{(value.Length - offset > 5 ? $" payload={Convert.ToHexString(value, offset + 5, value.Length - (offset + 5)).ToLowerInvariant()}" : string.Empty)}"));
            }
        }

        return segments;
    }

    private static string FormatPrivateWriteSegment(PrivateWriteSegment segment)
    {
        var details = new List<string>
        {
            $"offset={segment.Offset}",
            segment.Kind,
            segment.Shape,
        };

        if (segment.TargetRegister is ushort register)
        {
            details.Add($"target=0x{register:x4}");
            details.Add($"name={DescribePrivateRegister(register)}");
        }

        if (!string.IsNullOrWhiteSpace(segment.PayloadDescription))
            details.Add(segment.PayloadDescription);

        return string.Join(" ", details);
    }

    private static string DescribePrivateRegister(ushort register)
    {
        return register switch
        {
            0x0309 => "history synchronizations",
            0x0ffd => "mixed_settings 0xfd?",
            0x0ffe => "mixed_settings remaining_time",
            0x0fff => "mixed_settings soc",
            0x1000 => "settings 0x1000",
            0x1001 => "settings 0x1001",
            0x1002 => "settings 0x1002",
            0x1003 => "settings 0x1003",
            0x1004 => "settings 0x1004",
            0x1005 => "settings 0x1005",
            0x1006 => "settings 0x1006",
            0x1007 => "settings 0x1007",
            0x1008 => "settings 0x1008",
            0x1009 => "settings 0x1009",
            0x1029 => "sync-adjacent 0x1029?",
            0x102c => "synchronize?",
            0xeeb6 => "unknown 0xeeb6",
            0xec41 => "unknown 0xec41",
            0xed8c => "latest current",
            0xed8d => "latest voltage",
            0xed8e => "latest power",
            0xeeff => "latest_ext consumed_ah",
            _ => $"register_0x{register:x4}",
        };
    }

    private static async Task SendPublicKeepAliveAsync(DeviceSession session)
    {
        var characteristic = await session.GetCharacteristicAsync(PublicKeepAliveUuid);
        await characteristic.WriteValueAsync(PublicKeepAlivePayload, new Dictionary<string, object>());
        Console.WriteLine($"keepalive characteristic={PublicKeepAliveUuid} len={PublicKeepAlivePayload.Length} hex={Convert.ToHexString(PublicKeepAlivePayload).ToLowerInvariant()}");
    }

    private static string FormatPublicField(PublicField field, byte[] value)
    {
        var hex = Convert.ToHexString(value).ToLowerInvariant();
        var decoded = field.Decode(value, field.NotAvailableHex);
        return $"field key={field.Key} name={field.Name} uuid={field.Uuid} len={value.Length} hex={hex} value={decoded} unit={field.Unit}";
    }

    private static byte[] ParseHex(string hex)
    {
        var normalized = hex.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length == 0 || normalized.Length % 2 != 0)
            throw new ArgumentException($"Invalid hex payload: {hex}");

        return Convert.FromHexString(normalized);
    }

    private static string DecodeUnsignedHundredths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? "n/a" : (BitConverter.ToUInt16(value, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static string DecodeSignedHundredths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? "n/a" : (BitConverter.ToInt16(value, 0) / 100.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static string DecodeSignedTenths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? "n/a" : (BitConverter.ToInt32(value, 0) / 10.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    private static string DecodeNegatedSignedThousandths(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? "n/a" : (-BitConverter.ToInt32(value, 0) / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

    private static string DecodeNegatedSignedInt16(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? "n/a" : (-BitConverter.ToInt16(value, 0)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string DecodeSignedInt16(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? "n/a" : BitConverter.ToInt16(value, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string DecodeUnsignedInt16(byte[] value, string notAvailableHex)
        => MatchesNotAvailable(value, notAvailableHex) ? "n/a" : BitConverter.ToUInt16(value, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool MatchesNotAvailable(byte[] value, string notAvailableHex)
        => string.Equals(Convert.ToHexString(value), notAvailableHex, StringComparison.OrdinalIgnoreCase);

    private static string Flatten(Exception ex) => ex.GetBaseException().Message.Replace('\n', ' ');

    private static bool IsNotConnected(Exception ex)
        => ex.GetBaseException().Message.Contains("Not connected", StringComparison.OrdinalIgnoreCase);

    private static PrivateDecodedExportEntry? TryCreatePrivateDecodedExportEntry(DateTimeOffset timestamp, string characteristicUuid, string line)
    {
        if (!line.StartsWith("private ", StringComparison.Ordinal))
            return null;

        var stream = ExtractValue(line, "stream=", " ");
        var category = ExtractValue(line, "category=", " ");
        var command = ExtractValue(line, "command=", " ");
        var register = ExtractValue(line, "register=", " ");
        var kind = ExtractValue(line, "kind=", null);

        string? name = null;
        string? type = null;
        string? hex = null;
        string? value = null;

        if (!string.IsNullOrWhiteSpace(category))
        {
            name = ExtractValue(line, "name=", " len=");
            type = ExtractValue(line, "type=", " ");
            hex = ExtractValue(line, "hex=", " value=");
            value = ExtractValue(line, "value=", null);
        }
        else if (!string.IsNullOrWhiteSpace(register))
        {
            name = ExtractValue(line, "name=", " type=");
            type = ExtractValue(line, "type=", " ");
            hex = ExtractValue(line, "hex=", " value=");
            value = ExtractValue(line, "value=", null);
        }

        if (string.IsNullOrWhiteSpace(stream))
            return null;

        return new PrivateDecodedExportEntry(
            timestamp,
            characteristicUuid,
            stream,
            category,
            command,
            register,
            name,
            type,
            hex,
            value,
            kind,
            line);
    }

    private static string? ExtractValue(string input, string marker, string? endMarker)
    {
        var startIndex = input.IndexOf(marker, StringComparison.Ordinal);
        if (startIndex < 0)
            return null;

        startIndex += marker.Length;
        var endIndex = endMarker is null
            ? input.Length
            : input.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
            endIndex = input.Length;

        return endIndex <= startIndex ? null : input[startIndex..endIndex];
    }

    private sealed class PrivateMonitorSummary
    {
        private readonly Dictionary<string, PrivateRegisterObservation> _entries = new(StringComparer.Ordinal);

        public void Observe(IEnumerable<string> decodedLines)
        {
            foreach (var line in decodedLines)
            {
                var key = GetSummaryKey(line);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (_entries.TryGetValue(key, out var current))
                {
                    _entries[key] = current with { Count = current.Count + 1, LastFormatted = line };
                }
                else
                {
                    _entries[key] = new PrivateRegisterObservation(1, line);
                }
            }
        }

        public void WriteToConsole()
        {
            if (_entries.Count == 0)
                return;

            Console.WriteLine("private summary begin");
            foreach (var pair in _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                Console.WriteLine($"private summary key={pair.Key} count={pair.Value.Count} last={pair.Value.LastFormatted}");

            Console.WriteLine("private summary end");
        }

        private static string? GetSummaryKey(string line)
        {
            const string categoryMarker = "category=";
            const string commandMarker = "command=0x";
            const string registerMarker = "register=0x";

            var categoryIndex = line.IndexOf(categoryMarker, StringComparison.Ordinal);
            var commandIndex = line.IndexOf(commandMarker, StringComparison.Ordinal);
            if (categoryIndex >= 0 && commandIndex > categoryIndex)
            {
                var categoryStart = categoryIndex + categoryMarker.Length;
                var categoryEnd = line.IndexOf(' ', categoryStart);
                var commandStart = commandIndex + commandMarker.Length;
                var commandEnd = line.IndexOf(' ', commandStart);
                if (categoryEnd > categoryStart && commandEnd > commandStart)
                    return $"{line[categoryStart..categoryEnd]}:0x{line[commandStart..commandEnd]}";
            }

            var registerIndex = line.IndexOf(registerMarker, StringComparison.Ordinal);
            if (registerIndex >= 0)
            {
                var registerStart = registerIndex + registerMarker.Length;
                var registerEnd = line.IndexOf(' ', registerStart);
                if (registerEnd > registerStart)
                    return $"register:0x{line[registerStart..registerEnd]}";
            }

            return null;
        }
    }

    private sealed record PrivateRegisterObservation(int Count, string LastFormatted);

    private sealed record PrivateWriteSegment(int Offset, string Kind, string Shape, ushort? TargetRegister, string? PayloadDescription);

    private sealed class PrivateDecodedExportWriter(string path) : IDisposable
    {
        private readonly object _gate = new();
        private readonly StreamWriter _writer = CreateWriter(path);

        public void Write(DateTimeOffset timestamp, string characteristicUuid, IEnumerable<string> decodedLines)
        {
            lock (_gate)
            {
                foreach (var line in decodedLines)
                {
                    var entry = TryCreatePrivateDecodedExportEntry(timestamp, characteristicUuid, line);
                    if (entry is null)
                        continue;

                    _writer.WriteLine(JsonSerializer.Serialize(entry));
                }

                _writer.Flush();
            }
        }

        public void Dispose()
        {
            lock (_gate)
                _writer.Dispose();
        }

        private static StreamWriter CreateWriter(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            return new StreamWriter(File.Open(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read));
        }
    }

    private sealed record PrivateDecodedExportEntry(
        DateTimeOffset Timestamp,
        string CharacteristicUuid,
        string Stream,
        string? Category,
        string? Command,
        string? Register,
        string? Name,
        string? Type,
        string? Hex,
        string? Value,
        string? Kind,
        string RawLine);

    private sealed class DeviceSession(string address) : IAsyncDisposable
    {
        private Device? _device;

        public Device Device => _device ?? throw new InvalidOperationException("Session is not connected.");
        public string Address { get; } = address;

        public async Task ConnectAsync(Adapter adapter)
        {
            _device = await FindDeviceAsync(adapter, Address);

            Console.WriteLine($"device={await _device.GetAliasAsync()} ({await _device.GetAddressAsync()})");

            var lastError = default(Exception);
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Console.WriteLine($"connect_attempt={attempt} address={Address}");
                    await _device.ConnectAsync().WaitAsync(ConnectTimeout);
                    await _device.WaitForPropertyValueAsync("Connected", true, ConnectTimeout);
                    await _device.WaitForPropertyValueAsync("ServicesResolved", true, ConnectTimeout);
                    Console.WriteLine($"connected address={Address}");
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Console.WriteLine($"connect_failed attempt={attempt} address={Address} error={Flatten(ex)}");

                    try
                    {
                        await _device.DisconnectAsync();
                    }
                    catch
                    {
                    }

                    if (attempt < 3)
                        await Task.Delay(ConnectRetryDelay);
                }
            }

            throw new InvalidOperationException($"Failed to connect to {Address}.", lastError);
        }

        public async Task<IGattCharacteristic1> GetCharacteristicAsync(string characteristicUuid)
        {
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
            catch
            {
            }

            _device = null;
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
}

internal enum Command
{
    Services,
    Read,
    Notify,
    Write,
    VictronInit,
    PrivateMonitor,
    PublicSnapshot,
    PublicMonitor,
}

internal sealed record Options(
    Command Command,
    string Address,
    string Adapter,
    string CharacteristicUuid,
    string HexValue,
    string ExportPrivateDecodedPath,
    PrivateProfile PrivateProfile,
    int? Count,
    int IntervalSeconds,
    int DurationSeconds)
{
    private const string DefaultCharacteristicUuid = "97580002-ddf1-48be-b73e-182664615d8e";

    public static Options Parse(string[] args)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException(
                "Usage: HVO.Tools.SmartShuntBleConsole <services|read|notify|write|victron-init|private-monitor|public-snapshot|public-monitor> <BLE-MAC> [--adapter hci0] [--characteristic UUID] [--hex 0102] [--export-private-decoded path.jsonl] [--private-profile live|rich|reference|extended|syncprobe] [--count 1] [--interval-seconds 5] [--duration-seconds 30]");
        }

        var command = ParseCommand(args[0]);
        var address = args[1];
        var adapter = "hci0";
        var characteristicUuid = DefaultCharacteristicUuid;
        var hexValue = string.Empty;
        var exportPrivateDecodedPath = string.Empty;
        var privateProfile = PrivateProfile.Live;
        int? count = null;
        var intervalSeconds = 5;
        var durationSeconds = 30;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--adapter":
                    adapter = RequireValue(args, ref i, "--adapter");
                    break;
                case "--characteristic":
                    characteristicUuid = RequireValue(args, ref i, "--characteristic");
                    break;
                case "--hex":
                    hexValue = RequireValue(args, ref i, "--hex");
                    break;
                case "--export-private-decoded":
                    exportPrivateDecodedPath = RequireValue(args, ref i, "--export-private-decoded");
                    break;
                case "--private-profile":
                    privateProfile = ParsePrivateProfile(RequireValue(args, ref i, "--private-profile"));
                    break;
                case "--count":
                    count = int.Parse(RequireValue(args, ref i, "--count"));
                    break;
                case "--interval-seconds":
                    intervalSeconds = int.Parse(RequireValue(args, ref i, "--interval-seconds"));
                    break;
                case "--duration-seconds":
                    durationSeconds = int.Parse(RequireValue(args, ref i, "--duration-seconds"));
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }

        if (command == Command.Read && count is null)
            count = 1;

        if (command == Command.Write && string.IsNullOrWhiteSpace(hexValue))
            throw new ArgumentException("--hex is required for write.");

        if (command == Command.VictronInit || command == Command.PrivateMonitor)
            characteristicUuid = "306b0002-b081-4037-83dc-e59fcc3cdfd0";

        return new Options(command, address, adapter, characteristicUuid, hexValue, exportPrivateDecodedPath, privateProfile, count, intervalSeconds, durationSeconds);
    }

    private static Command ParseCommand(string value) => value.ToLowerInvariant() switch
    {
        "services" => Command.Services,
        "read" => Command.Read,
        "notify" => Command.Notify,
        "write" => Command.Write,
        "victron-init" => Command.VictronInit,
        "private-monitor" => Command.PrivateMonitor,
        "public-snapshot" => Command.PublicSnapshot,
        "public-monitor" => Command.PublicMonitor,
        _ => throw new ArgumentException($"Unknown command: {value}"),
    };

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {option}");

        index++;
        return args[index];
    }

    private static PrivateProfile ParsePrivateProfile(string value) => value.ToLowerInvariant() switch
    {
        "live" => PrivateProfile.Live,
        "rich" => PrivateProfile.Rich,
        "reference" => PrivateProfile.Reference,
        "extended" => PrivateProfile.Extended,
        "syncprobe" => PrivateProfile.SyncProbe,
        _ => throw new ArgumentException($"Unknown private profile: {value}"),
    };
}

internal enum PrivateProfile
{
    Live,
    Rich,
    Reference,
    Extended,
    SyncProbe,
}

internal sealed record PublicField(
    string Key,
    string Uuid,
    string Name,
    string Unit,
    Func<byte[], string, string> Decode,
    string NotAvailableHex);

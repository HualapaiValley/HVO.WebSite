using HVO.Gateway.TplinkKasa.Protocol;
using System.Net;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaReadOnlyProbe(IKasaLegacyClient client, KasaSystemInfoParser systemInfoParser, KasaEnergyParser energyParser, KasaCapabilityDetector capabilityDetector)
{
    private static readonly (string Name, string Command, KasaMetadataCapability Capability)[] MetadataCommands =
    [
        ("emeterDay", KasaCommands.GetEnergyDayStats(DateTime.UtcNow.Year, DateTime.UtcNow.Month), KasaMetadataCapability.EnergyTotal),
        ("emeterMonth", KasaCommands.GetEnergyMonthStats(DateTime.UtcNow.Year), KasaMetadataCapability.EnergyTotal),
        ("emeterGain", KasaCommands.GetEnergyGain, KasaMetadataCapability.Diagnostics),
        ("deviceIcon", KasaCommands.GetDeviceIcon, KasaMetadataCapability.Diagnostics),
        ("downloadState", KasaCommands.GetDownloadState, KasaMetadataCapability.FirmwareInfo),
        ("schedule", KasaCommands.GetScheduleRules, KasaMetadataCapability.ScheduleRead),
        ("scheduleNextAction", KasaCommands.GetNextScheduleAction, KasaMetadataCapability.ScheduleRead),
        ("countdown", KasaCommands.GetCountdownRules, KasaMetadataCapability.CountdownRead),
        ("away", KasaCommands.GetAwayRules, KasaMetadataCapability.AwayModeRead),
        ("time", KasaCommands.GetTime, KasaMetadataCapability.Diagnostics),
        ("timezone", KasaCommands.GetTimezone, KasaMetadataCapability.Diagnostics),
        ("cloud", KasaCommands.GetCloudInfo, KasaMetadataCapability.Diagnostics),
        ("cloudFirmware", KasaCommands.GetCloudFirmwareList, KasaMetadataCapability.FirmwareInfo),
        ("bulbLightState", KasaCommands.GetBulbLightState, KasaMetadataCapability.BulbLightRead),
        ("bulbLightDetails", KasaCommands.GetBulbLightDetails, KasaMetadataCapability.BulbLightRead),
        ("bulbDefaultBehavior", KasaCommands.GetBulbDefaultBehavior, KasaMetadataCapability.BulbLightRead),
        ("bulbCloud", KasaCommands.GetBulbCloudInfo, KasaMetadataCapability.Diagnostics),
        ("bulbTime", KasaCommands.GetBulbTime, KasaMetadataCapability.Diagnostics),
        ("bulbTimezone", KasaCommands.GetBulbTimezone, KasaMetadataCapability.Diagnostics),
        ("bulbSchedule", KasaCommands.GetBulbScheduleRules, KasaMetadataCapability.ScheduleRead),
        ("bulbScheduleNextAction", KasaCommands.GetBulbNextScheduleAction, KasaMetadataCapability.ScheduleRead),
        ("bulbEmeter", KasaCommands.GetBulbRealtimeEnergy, KasaMetadataCapability.EnergyRealtime),
        ("dimmerDefaultBehavior", KasaCommands.GetDimmerDefaultBehavior, KasaMetadataCapability.DimmerRead),
        ("dimmerParameters", KasaCommands.GetDimmerParameters, KasaMetadataCapability.DimmerRead)
    ];

    private static readonly (string Name, string Command, KasaMetadataCapability Capability)[] PrivacySensitiveCommands =
    [
        ("wifiScan", KasaCommands.GetCachedWifiScanInfo, KasaMetadataCapability.WifiScanRead)
    ];

    public Task<KasaProbeResult> ProbeAsync(string host, int port, CancellationToken cancellationToken) =>
        ProbeAsync(host, port, includePrivacySensitive: false, cancellationToken);

    public async Task<KasaProbeResult> ProbeAsync(string host, int port, bool includePrivacySensitive, CancellationToken cancellationToken)
    {
        try
        {
            using var sysinfoResponse = await client.SendReadOnlyAsync(host, port, KasaCommands.GetSystemInfo, cancellationToken)
                .ConfigureAwait(false);
            var sysinfo = systemInfoParser.Parse(sysinfoResponse);
            var sysinfoShape = KasaJsonShapeSummarizer.Summarize(sysinfoResponse.RootElement);

            KasaEnergyReading? energy = null;
            var metadata = new Dictionary<string, KasaReadOnlyModuleResult>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var energyResponse = await client.SendReadOnlyAsync(host, port, KasaCommands.GetRealtimeEnergy, cancellationToken)
                    .ConfigureAwait(false);
                energy = energyParser.Parse(energyResponse);
                metadata["emeter"] = KasaReadOnlyModuleResult.FromResponse("emeter", energyResponse.RootElement);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidDataException or JsonException or System.Net.Sockets.SocketException)
            {
                metadata["emeter"] = KasaReadOnlyModuleResult.Failed("emeter", KasaFailureMessages.DescribeReadFailure(ex));
            }

            foreach (var (name, command, capability) in MetadataCommands.Concat(includePrivacySensitive ? PrivacySensitiveCommands : []))
            {
                try
                {
                    using var response = await client.SendReadOnlyAsync(host, port, command, cancellationToken).ConfigureAwait(false);
                    metadata[name] = KasaReadOnlyModuleResult.FromResponse(name, response.RootElement);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidDataException or JsonException or System.Net.Sockets.SocketException)
                {
                    metadata[name] = KasaReadOnlyModuleResult.Failed(name, KasaFailureMessages.DescribeReadFailure(ex));
                }

                if (metadata[name].IsSupported)
                {
                    metadata[name] = metadata[name] with { Capability = capability };
                }
            }

            foreach (var childCommand in BuildChildMetadataCommands(sysinfo.Children))
            {
                try
                {
                    using var response = await client.SendReadOnlyAsync(host, port, childCommand.Command, cancellationToken).ConfigureAwait(false);
                    metadata[childCommand.Name] = KasaReadOnlyModuleResult.FromResponse(childCommand.Name, response.RootElement);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidDataException or JsonException or System.Net.Sockets.SocketException)
                {
                    metadata[childCommand.Name] = KasaReadOnlyModuleResult.Failed(childCommand.Name, KasaFailureMessages.DescribeReadFailure(ex));
                }

                if (metadata[childCommand.Name].IsSupported)
                {
                    metadata[childCommand.Name] = metadata[childCommand.Name] with { Capability = childCommand.Capability };
                }
            }

            var profile = capabilityDetector.Detect(sysinfo, null, energy);
            var capabilities = new HashSet<KasaCapability>(profile.Capabilities);
            var metadataCapabilities = new HashSet<KasaMetadataCapability>(profile.MetadataCapabilities);
            if (HasLedOff(sysinfo.RawSystemInfo))
            {
                metadata["led"] = KasaReadOnlyModuleResult.Supported("led", KasaMetadataCapability.LedRead, KasaJsonShapeSummarizer.Summarize(sysinfo.RawSystemInfo));
            }

            foreach (var module in metadata.Values)
            {
                if (module.IsSupported && module.Capability is { } capability)
                {
                    metadataCapabilities.Add(capability);
                    if (capability is KasaMetadataCapability.ScheduleRead or KasaMetadataCapability.CountdownRead or KasaMetadataCapability.AwayModeRead)
                    {
                        capabilities.Add(KasaCapability.ScheduleMetadata);
                    }
                    else if (capability == KasaMetadataCapability.LedRead)
                    {
                        capabilities.Add(KasaCapability.LedState);
                    }
                    else if (capability == KasaMetadataCapability.BulbLightRead)
                    {
                        capabilities.Add(KasaCapability.LightState);
                    }
                    else if (capability == KasaMetadataCapability.DimmerRead)
                    {
                        capabilities.Add(KasaCapability.Dimming);
                    }
                }
            }

            profile = profile with { Capabilities = capabilities, MetadataCapabilities = metadataCapabilities };
            return KasaProbeResult.Success(host, port, sysinfo, sysinfoShape, energy, profile, metadata);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidDataException or JsonException or System.Net.Sockets.SocketException)
        {
            return KasaProbeResult.Failed(host, port, KasaFailureMessages.DescribeReadFailure(ex));
        }
    }

    private static IEnumerable<(string Name, string Command, KasaMetadataCapability Capability)> BuildChildMetadataCommands(IReadOnlyList<KasaChildInfo> children)
    {
        var year = DateTime.UtcNow.Year;
        var month = DateTime.UtcNow.Month;
        for (var index = 0; index < children.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(children[index].Id))
            {
                continue;
            }

            var label = $"child{index + 1}";
            var childId = children[index].Id!;
            yield return ($"{label}:emeter", KasaCommands.WithChildContext(childId, KasaCommands.GetRealtimeEnergy), KasaMetadataCapability.EnergyRealtime);
            yield return ($"{label}:emeterDay", KasaCommands.WithChildContext(childId, KasaCommands.GetEnergyDayStats(year, month)), KasaMetadataCapability.EnergyTotal);
            yield return ($"{label}:emeterMonth", KasaCommands.WithChildContext(childId, KasaCommands.GetEnergyMonthStats(year)), KasaMetadataCapability.EnergyTotal);
            yield return ($"{label}:schedule", KasaCommands.WithChildContext(childId, KasaCommands.GetScheduleRules), KasaMetadataCapability.ScheduleRead);
            yield return ($"{label}:scheduleNextAction", KasaCommands.WithChildContext(childId, KasaCommands.GetNextScheduleAction), KasaMetadataCapability.ScheduleRead);
            yield return ($"{label}:countdown", KasaCommands.WithChildContext(childId, KasaCommands.GetCountdownRules), KasaMetadataCapability.CountdownRead);
            yield return ($"{label}:away", KasaCommands.WithChildContext(childId, KasaCommands.GetAwayRules), KasaMetadataCapability.AwayModeRead);
        }
    }

    public Task<IReadOnlyList<KasaProbeResult>> ScanCidrAsync(string cidr, int port, int maxConcurrency, CancellationToken cancellationToken) =>
        ScanCidrAsync(cidr, port, maxConcurrency, includePrivacySensitive: false, cancellationToken);

    public async Task<IReadOnlyList<KasaProbeResult>> ScanCidrAsync(string cidr, int port, int maxConcurrency, bool includePrivacySensitive, CancellationToken cancellationToken)
    {
        var scanOptions = new KasaReadOnlyScanOptions();
        return await ScanCidrAsync(cidr, port, maxConcurrency, scanOptions, includePrivacySensitive, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KasaProbeResult>> ScanCidrAsync(
        string cidr,
        int port,
        int maxConcurrency,
        KasaReadOnlyScanOptions scanOptions,
        bool includePrivacySensitive,
        CancellationToken cancellationToken)
    {
        ValidateScanRequest(cidr, maxConcurrency, scanOptions);
        var hosts = EnumerateIpv4Hosts(cidr).ToArray();
        var results = new List<KasaProbeResult>();
        using var throttler = new SemaphoreSlim(maxConcurrency);

        var tasks = hosts.Select(async host =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await ProbeAsync(host, port, includePrivacySensitive, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    lock (results)
                    {
                        results.Add(result);
                    }
                }
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OrderBy(r => IPAddress.Parse(r.Host).GetAddressBytes(), ByteArrayComparer.Instance).ToArray();
    }

    private static void ValidateScanRequest(string cidr, int maxConcurrency, KasaReadOnlyScanOptions scanOptions)
    {
        if (scanOptions.MaxHosts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scanOptions), "Scan host limit must be at least 1.");
        }

        if (scanOptions.MaxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scanOptions), "Scan concurrency limit must be at least 1.");
        }

        if (maxConcurrency is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Scan concurrency must be at least 1.");
        }

        if (maxConcurrency > scanOptions.MaxConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), $"Scan concurrency must not exceed {scanOptions.MaxConcurrency}.");
        }

        var requested = Ipv4CidrRange.Parse(cidr);
        if (requested.HostCount > (ulong)scanOptions.MaxHosts)
        {
            throw new ArgumentOutOfRangeException(nameof(cidr), $"Scan CIDR contains {requested.HostCount} usable hosts; maximum allowed is {scanOptions.MaxHosts}.");
        }

        if (!scanOptions.RequireConfiguredNetwork)
        {
            return;
        }

        if (scanOptions.AllowedCidrs.Count == 0)
        {
            throw new InvalidOperationException("At least one configured network CIDR is required before scanning.");
        }

        foreach (var allowedCidr in scanOptions.AllowedCidrs)
        {
            var allowed = Ipv4CidrRange.Parse(allowedCidr);
            if (allowed.Contains(requested))
            {
                return;
            }
        }

        throw new InvalidOperationException("Scan CIDR must be contained within a configured network CIDR.");
    }

    private static IEnumerable<string> EnumerateIpv4Hosts(string cidr)
    {
        var range = Ipv4CidrRange.Parse(cidr);
        var first = range.FirstHost;
        var last = range.LastHost;

        for (var value = first; value <= last; value++)
        {
            yield return new IPAddress(
            [
                (byte)((value >> 24) & 0xff),
                (byte)((value >> 16) & 0xff),
                (byte)((value >> 8) & 0xff),
                (byte)(value & 0xff)
            ]).ToString();

            if (value == uint.MaxValue)
            {
                break;
            }
        }
    }

    private readonly record struct Ipv4CidrRange(uint Start, uint End, int Prefix)
    {
        public uint FirstHost => Prefix >= 31 ? Start : Start + 1;

        public uint LastHost => Prefix >= 31 ? End : End - 1;

        public ulong HostCount => LastHost < FirstHost ? 0 : (ulong)LastHost - FirstHost + 1;

        public static Ipv4CidrRange Parse(string cidr)
        {
            var parts = cidr.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var networkAddress) || !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            {
                throw new ArgumentException($"Invalid IPv4 CIDR: {cidr}.", nameof(cidr));
            }

            var bytes = networkAddress.GetAddressBytes();
            if (bytes.Length != 4)
            {
                throw new ArgumentException($"Only IPv4 CIDR ranges are supported: {cidr}.", nameof(cidr));
            }

            var network = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            var start = network & mask;
            var end = start | ~mask;
            return new Ipv4CidrRange(start, end, prefix);
        }

        public bool Contains(Ipv4CidrRange other) => other.Start >= Start && other.End <= End;
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                var comparison = x[i].CompareTo(y[i]);
                if (comparison != 0) return comparison;
            }

            return x.Length.CompareTo(y.Length);
        }
    }

    private static bool HasLedOff(JsonElement sysinfo) =>
        sysinfo.ValueKind == JsonValueKind.Object
        && sysinfo.TryGetProperty("led_off", out var ledOff)
        && ledOff.ValueKind == JsonValueKind.Number
        && ledOff.TryGetInt32(out var value)
        && value is 0 or 1;
}

public sealed class KasaReadOnlyScanOptions
{
    public const int DefaultMaxHosts = 256;
    public const int DefaultMaxConcurrency = 64;

    public int MaxHosts { get; init; } = DefaultMaxHosts;

    public int MaxConcurrency { get; init; } = DefaultMaxConcurrency;

    public bool RequireConfiguredNetwork { get; init; } = true;

    public IReadOnlyList<string> AllowedCidrs { get; init; } = [];
}

public sealed record KasaProbeResult(
    bool IsSuccess,
    string Host,
    int Port,
    string? FailureReason,
    KasaSystemInfo? SystemInfo,
    IReadOnlyList<KasaJsonFieldShape> SystemInfoShape,
    KasaEnergyReading? Energy,
    KasaDeviceProfile? Profile,
    IReadOnlyDictionary<string, KasaReadOnlyModuleResult> Metadata)
{
    public static KasaProbeResult Success(
        string host,
        int port,
        KasaSystemInfo systemInfo,
        IReadOnlyList<KasaJsonFieldShape> systemInfoShape,
        KasaEnergyReading? energy,
        KasaDeviceProfile profile,
        IReadOnlyDictionary<string, KasaReadOnlyModuleResult> metadata) =>
        new(true, host, port, null, systemInfo, systemInfoShape, energy, profile, metadata);

    public static KasaProbeResult Failed(string host, int port, string reason) =>
        new(false, host, port, reason, null, [], null, null, new Dictionary<string, KasaReadOnlyModuleResult>());
}

public sealed record KasaReadOnlyModuleResult(
    string Name,
    bool IsSupported,
    int? ErrorCode,
    string? ErrorMessage,
    KasaMetadataCapability? Capability,
    IReadOnlyList<KasaJsonFieldShape> Shape)
{
    public static KasaReadOnlyModuleResult FromResponse(string name, JsonElement response)
    {
        var errCode = FindFirstErrCode(response);
        return new KasaReadOnlyModuleResult(name, errCode.GetValueOrDefault(0) == 0, errCode, null, null, KasaJsonShapeSummarizer.Summarize(response));
    }

    public static KasaReadOnlyModuleResult Supported(string name, KasaMetadataCapability capability, IReadOnlyList<KasaJsonFieldShape> shape) =>
        new(name, true, 0, null, capability, shape);

    public static KasaReadOnlyModuleResult Failed(string name, string error) => new(name, false, null, error, null, []);

    private static int? FindFirstErrCode(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("err_code", out var errCode) && KasaJson.TryGetInt(errCode, out var value))
            {
                return value;
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindFirstErrCode(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}

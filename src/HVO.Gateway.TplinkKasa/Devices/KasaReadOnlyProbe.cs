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
        ("schedule", KasaCommands.GetScheduleRules, KasaMetadataCapability.ScheduleRead),
        ("scheduleNextAction", KasaCommands.GetNextScheduleAction, KasaMetadataCapability.ScheduleRead),
        ("countdown", KasaCommands.GetCountdownRules, KasaMetadataCapability.CountdownRead),
        ("away", KasaCommands.GetAwayRules, KasaMetadataCapability.AwayModeRead),
        ("led", KasaCommands.GetLedState, KasaMetadataCapability.LedRead),
        ("time", KasaCommands.GetTime, KasaMetadataCapability.Diagnostics),
        ("timezone", KasaCommands.GetTimezone, KasaMetadataCapability.Diagnostics),
        ("cloud", KasaCommands.GetCloudInfo, KasaMetadataCapability.Diagnostics)
    ];

    public async Task<KasaProbeResult> ProbeAsync(string host, int port, CancellationToken cancellationToken)
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
                metadata["emeter"] = KasaReadOnlyModuleResult.Failed("emeter", ex.Message);
            }

            foreach (var (name, command, capability) in MetadataCommands)
            {
                try
                {
                    using var response = await client.SendReadOnlyAsync(host, port, command, cancellationToken).ConfigureAwait(false);
                    metadata[name] = KasaReadOnlyModuleResult.FromResponse(name, response.RootElement);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidDataException or JsonException or System.Net.Sockets.SocketException)
                {
                    metadata[name] = KasaReadOnlyModuleResult.Failed(name, ex.Message);
                }

                if (metadata[name].IsSupported)
                {
                    metadata[name] = metadata[name] with { Capability = capability };
                }
            }

            var profile = capabilityDetector.Detect(sysinfo, null, energy);
            var capabilities = new HashSet<KasaCapability>(profile.Capabilities);
            var metadataCapabilities = new HashSet<KasaMetadataCapability>(profile.MetadataCapabilities);
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
                }
            }

            profile = profile with { Capabilities = capabilities, MetadataCapabilities = metadataCapabilities };
            return KasaProbeResult.Success(host, port, sysinfo, sysinfoShape, energy, profile, metadata);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidDataException or JsonException or System.Net.Sockets.SocketException)
        {
            return KasaProbeResult.Failed(host, port, ex.Message);
        }
    }

    public async Task<IReadOnlyList<KasaProbeResult>> ScanCidrAsync(string cidr, int port, int maxConcurrency, CancellationToken cancellationToken)
    {
        var hosts = EnumerateIpv4Hosts(cidr).ToArray();
        var results = new List<KasaProbeResult>();
        using var throttler = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        var tasks = hosts.Select(async host =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await ProbeAsync(host, port, cancellationToken).ConfigureAwait(false);
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

    private static IEnumerable<string> EnumerateIpv4Hosts(string cidr)
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

        var first = prefix >= 31 ? start : start + 1;
        var last = prefix >= 31 ? end : end - 1;

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

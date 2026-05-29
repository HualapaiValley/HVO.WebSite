using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Protocol;
using System.Text.Json;

var command = args.FirstOrDefault()?.ToLowerInvariant();
if (command is not "probe" and not "scan")
{
    PrintUsage();
    return command is null ? 0 : 2;
}

var options = ParseOptions(args.Skip(1).ToArray());
var port = GetIntOption(options, "port", 9999);
var timeoutSeconds = GetIntOption(options, "timeout", 3);
var includeIdentifiers = GetBoolOption(options, "include-identifiers", false);
var includeLocators = GetBoolOption(options, "include-locators", false);
var includePrivacySensitive = GetBoolOption(options, "include-privacy-sensitive", false);
var summary = GetBoolOption(options, "summary", false);
var shapes = GetBoolOption(options, "shapes", false);
var client = new KasaLegacyClient(TimeSpan.FromSeconds(timeoutSeconds));
var probe = new KasaReadOnlyProbe(client, new KasaSystemInfoParser(), new KasaEnergyParser(), new KasaCapabilityDetector());

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(GetIntOption(options, "overall-timeout", command == "scan" ? 60 : 15)));

if (command == "probe")
{
    if (!options.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
    {
        Console.Error.WriteLine("--host is required for probe.");
        return 2;
    }

    var result = await probe.ProbeAsync(host, port, includePrivacySensitive, cts.Token);
    WriteProbeResult(result, includeIdentifiers, includeLocators, shapes);
    return result.IsSuccess ? 0 : 1;
}

if (!options.TryGetValue("cidr", out var cidr) || string.IsNullOrWhiteSpace(cidr))
{
    Console.Error.WriteLine("--cidr is required for scan.");
    return 2;
}

var results = await probe.ScanCidrAsync(cidr, port, GetIntOption(options, "concurrency", 32), includePrivacySensitive, cts.Token);
if (summary)
{
    WriteScanSummary(results, shapes);
    return results.Count > 0 ? 0 : 1;
}

foreach (var result in results)
{
    WriteProbeResult(result, includeIdentifiers, includeLocators, shapes);
}

return results.Count > 0 ? 0 : 1;

static void PrintUsage()
{
    Console.WriteLine("TP-Link/Kasa read-only prototype utility");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa -- probe --host <ip-or-host> [--port 9999]");
    Console.WriteLine("  dotnet run --project src/HVO.Gateway.TplinkKasa -- scan --cidr <x.x.x.x/nn> [--port 9999] [--concurrency 32] [--summary true] [--shapes true] [--include-privacy-sensitive true]");
    Console.WriteLine();
    Console.WriteLine("Only allowlisted read-only Kasa commands are sent. Wi-Fi scan shapes require --include-privacy-sensitive true and never print raw values unless future code explicitly adds them.");
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < values.Length; i++)
    {
        var value = values[i];
        if (!value.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = value[2..];
        if (i + 1 < values.Length && !values[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[key] = values[++i];
        }
        else
        {
            result[key] = "true";
        }
    }

    return result;
}

static int GetIntOption(Dictionary<string, string> options, string name, int defaultValue) =>
    options.TryGetValue(name, out var text) && int.TryParse(text, out var value) ? value : defaultValue;

static bool GetBoolOption(Dictionary<string, string> options, string name, bool defaultValue) =>
    options.TryGetValue(name, out var text) && bool.TryParse(text, out var value) ? value : defaultValue;

static void WriteProbeResult(KasaProbeResult result, bool includeIdentifiers, bool includeLocators, bool includeShapes)
{
    var deviceId = result.SystemInfo?.DeviceId;
    var macAddress = KasaJson.NormalizeMacAddress(result.SystemInfo?.MacAddress);
    var output = new
    {
        result.IsSuccess,
        Host = includeLocators ? result.Host : null,
        HostPresent = !string.IsNullOrWhiteSpace(result.Host),
        HostHash = includeLocators ? null : ShortHash(result.Host),
        result.Port,
        result.FailureReason,
        DeviceId = includeIdentifiers ? deviceId : null,
        DeviceIdPresent = !string.IsNullOrWhiteSpace(deviceId),
        DeviceIdHash = includeIdentifiers ? null : ShortHash(deviceId),
        MacAddress = includeIdentifiers ? macAddress : null,
        MacAddressPresent = !string.IsNullOrWhiteSpace(macAddress),
        MacAddressHash = includeIdentifiers ? null : ShortHash(macAddress),
        result.SystemInfo?.Model,
        result.SystemInfo?.HardwareVersion,
        result.SystemInfo?.SoftwareVersion,
        Kind = result.Profile?.DeviceKind.ToString(),
        Capabilities = result.Profile?.Capabilities.Select(x => x.ToString()).Order().ToArray(),
        MetadataCapabilities = result.Profile?.MetadataCapabilities.Select(x => x.ToString()).Order().ToArray(),
        SystemInfoShape = includeShapes ? result.SystemInfoShape : null,
        Energy = result.Energy is null ? null : new
        {
            result.Energy.PowerW,
            result.Energy.VoltageV,
            result.Energy.CurrentA,
            result.Energy.EnergyKWh
        },
        Metadata = result.Metadata.ToDictionary(
            x => x.Key,
            x => new
            {
                x.Value.IsSupported,
                x.Value.ErrorCode,
                x.Value.ErrorMessage,
                Capability = x.Value.Capability?.ToString(),
                Shape = includeShapes ? x.Value.Shape : null
            })
    };

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}

static void WriteScanSummary(IReadOnlyList<KasaProbeResult> results, bool includeShapes)
{
    static string CapabilityName(KasaCapability capability) => capability.ToString();
    static string MetadataName(KasaMetadataCapability capability) => capability.ToString();

    var successful = results.Where(x => x.IsSuccess).ToArray();
    var output = new
    {
        TotalResponders = successful.Length,
        DeviceIdPresent = successful.Count(x => !string.IsNullOrWhiteSpace(x.SystemInfo?.DeviceId)),
        MacAddressPresent = successful.Count(x => !string.IsNullOrWhiteSpace(x.SystemInfo?.MacAddress)),
        EnergySupported = successful.Count(x => x.Energy is not null),
        Models = successful
            .GroupBy(x => x.SystemInfo?.Model ?? "Unknown")
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Count()),
        Kinds = successful
            .GroupBy(x => x.Profile?.DeviceKind.ToString() ?? "Unknown")
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Count()),
        Capabilities = successful
            .SelectMany(x => x.Profile?.Capabilities.Select(CapabilityName) ?? [])
            .GroupBy(x => x)
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Count()),
        MetadataCapabilities = successful
            .SelectMany(x => x.Profile?.MetadataCapabilities.Select(MetadataName) ?? [])
            .GroupBy(x => x)
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Count()),
        MetadataModules = successful
            .SelectMany(x => x.Metadata.Select(module => new { module.Key, module.Value.IsSupported }))
            .Where(x => x.IsSupported)
            .GroupBy(x => x.Key)
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Count()),
        Shapes = includeShapes ? BuildShapeSummary(successful) : null
    };

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}

static object BuildShapeSummary(IReadOnlyList<KasaProbeResult> results)
{
    return results
        .GroupBy(result => new
        {
            Model = result.SystemInfo?.Model ?? "Unknown",
            Hardware = result.SystemInfo?.HardwareVersion ?? "Unknown",
            Software = result.SystemInfo?.SoftwareVersion ?? "Unknown"
        })
        .OrderBy(group => group.Key.Model)
        .ThenBy(group => group.Key.Hardware)
        .ThenBy(group => group.Key.Software)
        .Select(group => new
        {
            group.Key.Model,
            group.Key.Hardware,
            group.Key.Software,
            Count = group.Count(),
            SystemInfoFields = MergeShapes(group.SelectMany(result => result.SystemInfoShape)),
            Modules = group
                .SelectMany(result => result.Metadata.Values)
                .GroupBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(moduleGroup => moduleGroup.Key)
                .Select(moduleGroup => new
                {
                    Name = moduleGroup.Key,
                    Supported = moduleGroup.Count(module => module.IsSupported),
                    Unsupported = moduleGroup.Count(module => !module.IsSupported),
                    ErrorCodes = moduleGroup.Select(module => module.ErrorCode).Where(errorCode => errorCode is not null).Distinct().Order().ToArray(),
                    Fields = MergeShapes(moduleGroup.SelectMany(module => module.Shape))
                })
                .ToArray()
        })
        .ToArray();
}

static IReadOnlyList<KasaJsonFieldShape> MergeShapes(IEnumerable<KasaJsonFieldShape> shapes) =>
    shapes
        .GroupBy(shape => shape.Path, StringComparer.Ordinal)
        .Select(group => new KasaJsonFieldShape(
            group.Key,
            string.Join("|", group.Select(shape => shape.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))))
        .OrderBy(shape => shape.Path, StringComparer.Ordinal)
        .ToArray();

static string? ShortHash(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
}

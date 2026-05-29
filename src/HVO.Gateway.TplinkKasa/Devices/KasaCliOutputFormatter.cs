using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public static class KasaCliOutputFormatter
{
    public static string FormatProbeResult(KasaProbeResult result, bool includeIdentifiers, bool includeLocators, bool includeShapes)
    {
        var deviceId = result.SystemInfo?.DeviceId;
        var macAddress = KasaJson.NormalizeMacAddress(result.SystemInfo?.MacAddress);
        var output = new
        {
            result.IsSuccess,
            Host = includeLocators ? result.Host : null,
            HostPresent = !string.IsNullOrWhiteSpace(result.Host),
            result.Port,
            result.FailureReason,
            DeviceId = includeIdentifiers ? deviceId : null,
            DeviceIdPresent = !string.IsNullOrWhiteSpace(deviceId),
            MacAddress = includeIdentifiers ? macAddress : null,
            MacAddressPresent = !string.IsNullOrWhiteSpace(macAddress),
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

        return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatScanSummary(IReadOnlyList<KasaProbeResult> results, bool includeShapes)
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

        return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object BuildShapeSummary(IReadOnlyList<KasaProbeResult> results)
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

    private static IReadOnlyList<KasaJsonFieldShape> MergeShapes(IEnumerable<KasaJsonFieldShape> shapes) =>
        shapes
            .GroupBy(shape => shape.Path, StringComparer.Ordinal)
            .Select(group => new KasaJsonFieldShape(
                group.Key,
                string.Join("|", group.Select(shape => shape.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))))
            .OrderBy(shape => shape.Path, StringComparer.Ordinal)
            .ToArray();
}

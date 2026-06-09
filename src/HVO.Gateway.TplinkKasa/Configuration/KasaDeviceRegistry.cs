using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Configuration;

public sealed class KasaDeviceRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IOptions<KasaGatewayOptions> options;
    private readonly IWebHostEnvironment environment;
    private readonly SemaphoreSlim gate = new(1, 1);
    private List<KasaDeviceConfig>? devices;

    public KasaDeviceRegistry(IOptions<KasaGatewayOptions> options, IWebHostEnvironment environment)
    {
        this.options = options;
        this.environment = environment;
    }

    public async Task<IReadOnlyList<KasaDeviceConfig>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return devices!.Select(Clone).ToArray();
    }

    public async Task<IReadOnlyList<KasaDeviceConfig>> GetEnabledDevicesAsync(CancellationToken cancellationToken = default)
    {
        var current = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        return current.Where(device => device.Enabled).ToArray();
    }

    public async Task<KasaDeviceConfig> AddOrUpdateAsync(KasaDeviceConfig device, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = Normalize(device);
            var index = devices!.FindIndex(existing =>
                string.Equals(existing.EffectiveSourceId, normalized.EffectiveSourceId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(existing.DeviceId) && string.Equals(existing.DeviceId, normalized.DeviceId, StringComparison.OrdinalIgnoreCase)));

            if (index >= 0)
            {
                devices[index] = normalized;
            }
            else
            {
                devices.Add(normalized);
            }

            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            return Clone(normalized);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = devices!.RemoveAll(device => string.Equals(device.EffectiveSourceId, sourceId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (devices is not null)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (devices is not null)
            {
                return;
            }

            var path = GetRegistryPath();
            if (!File.Exists(path))
            {
                devices = options.Value.Devices.Select(Clone).ToList();
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<KasaDeviceRegistryDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            devices = loaded?.Devices?.Select(Normalize).ToList() ?? [];
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveLockedAsync(CancellationToken cancellationToken)
    {
        var path = GetRegistryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new KasaDeviceRegistryDocument(devices!.OrderBy(device => device.EffectiveSourceId, StringComparer.OrdinalIgnoreCase).ToList());
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private string GetRegistryPath()
    {
        var configured = options.Value.DeviceRegistryPath;
        return Path.IsPathFullyQualified(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    private static KasaDeviceConfig Normalize(KasaDeviceConfig device)
    {
        var normalized = Clone(device);
        normalized.DeviceId = normalized.DeviceId.Trim();
        normalized.SourceId = normalized.SourceId.Trim();
        normalized.DisplayName = string.IsNullOrWhiteSpace(normalized.DisplayName) ? null : normalized.DisplayName.Trim();
        normalized.GroupName = string.IsNullOrWhiteSpace(normalized.GroupName) ? null : normalized.GroupName.Trim();
        normalized.Host = normalized.Host.Trim();
        normalized.MacAddress = string.IsNullOrWhiteSpace(normalized.MacAddress) ? null : normalized.MacAddress.Trim();
        normalized.NetworkName = string.IsNullOrWhiteSpace(normalized.NetworkName) ? null : normalized.NetworkName.Trim();
        normalized.DisplayTimeZoneId = string.IsNullOrWhiteSpace(normalized.DisplayTimeZoneId) ? null : normalized.DisplayTimeZoneId.Trim();
        normalized.ExpectedModel = string.IsNullOrWhiteSpace(normalized.ExpectedModel) ? null : normalized.ExpectedModel.Trim();
        normalized.ExpectedHardwareVersion = string.IsNullOrWhiteSpace(normalized.ExpectedHardwareVersion) ? null : normalized.ExpectedHardwareVersion.Trim();
        normalized.ExpectedSoftwareVersion = string.IsNullOrWhiteSpace(normalized.ExpectedSoftwareVersion) ? null : normalized.ExpectedSoftwareVersion.Trim();
        return normalized;
    }

    private static KasaDeviceConfig Clone(KasaDeviceConfig device) => new()
    {
        Enabled = device.Enabled,
        DeviceId = device.DeviceId,
        SourceId = device.SourceId,
        DisplayName = device.DisplayName,
        GroupName = device.GroupName,
        IsFavorite = device.IsFavorite,
        Host = device.Host,
        Port = device.Port,
        MacAddress = device.MacAddress,
        NetworkName = device.NetworkName,
        DisplayTimeZoneId = device.DisplayTimeZoneId,
        ExpectedModel = device.ExpectedModel,
        ExpectedHardwareVersion = device.ExpectedHardwareVersion,
        ExpectedSoftwareVersion = device.ExpectedSoftwareVersion,
        ExpectedChildCount = device.ExpectedChildCount,
        ProtocolFamily = device.ProtocolFamily,
        DeviceKind = device.DeviceKind,
        Capabilities = [.. device.Capabilities],
        MetadataCapabilities = [.. device.MetadataCapabilities],
        CommandCapabilities = [.. device.CommandCapabilities],
        SafetyClass = device.SafetyClass,
        PollIntervalSeconds = device.PollIntervalSeconds
    };

    private sealed record KasaDeviceRegistryDocument(List<KasaDeviceConfig> Devices);
}
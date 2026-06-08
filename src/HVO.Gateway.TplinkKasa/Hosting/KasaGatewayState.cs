using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HVO.Gateway.TplinkKasa.Hosting;

public sealed class KasaGatewayState(IOptions<KasaGatewayOptions> options, KasaDeviceRegistry registry)
{
    private readonly ConcurrentDictionary<string, KasaDeviceStatus> _devices = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastPollStartedAtUtc { get; private set; }

    public DateTimeOffset? LastPollCompletedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public void MarkPollStarted()
    {
        LastPollStartedAtUtc = DateTimeOffset.UtcNow;
        LastError = null;
    }

    public void MarkPollCompleted() => LastPollCompletedAtUtc = DateTimeOffset.UtcNow;

    public void MarkPollFailed(string error) => LastError = error;

    public void ApplyResult(KasaDeviceConfig config, KasaPollResult result)
    {
        var status = result.Snapshot is null
            ? KasaDeviceStatus.Offline(config, result.FailureReason ?? "Polling failed.")
            : KasaDeviceStatus.Online(config, result.Snapshot, result.DegradedReason);

        if (result.Snapshot?.ReadMetadata is null
            && _devices.TryGetValue(config.EffectiveSourceId, out var existing)
            && existing.DeviceInfo is not null)
        {
            status = status with
            {
                DeviceInfo = MergeDeviceInfo(status.DeviceInfo, existing.DeviceInfo),
                ReadMetadata = existing.ReadMetadata
            };
        }

        _devices[config.EffectiveSourceId] = status;
    }

    private static KasaDeviceInfo? MergeDeviceInfo(KasaDeviceInfo? current, KasaDeviceInfo previous)
    {
        if (current is null)
        {
            return previous;
        }

        return current with
        {
            DeviceTime = current.DeviceTime ?? previous.DeviceTime,
            Timezone = current.Timezone ?? previous.Timezone,
            DeviceUtcOffsetMinutes = current.DeviceUtcOffsetMinutes ?? previous.DeviceUtcOffsetMinutes,
            DeviceTimeZoneLabel = current.DeviceTimeZoneLabel ?? previous.DeviceTimeZoneLabel,
            Cloud = current.Cloud ?? previous.Cloud,
            FirmwareDownload = current.FirmwareDownload ?? previous.FirmwareDownload,
            CloudFirmware = current.CloudFirmware ?? previous.CloudFirmware,
            Dimmer = current.Dimmer ?? previous.Dimmer
        };
    }

    public async Task<KasaGatewayStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var configuredDevices = await registry.GetEnabledDevicesAsync(cancellationToken).ConfigureAwait(false);
        var devices = BuildDeviceList(configuredDevices);
        return new KasaGatewayStatusResponse(
            options.Value.GatewayId,
            options.Value.DisplayTimeZoneId,
            configuredDevices.Count,
            devices.Count(device => device.IsOnline),
            devices.Count(device => device.IsDegraded),
            LastPollStartedAtUtc,
            LastPollCompletedAtUtc,
            LastError,
            devices);
    }

    public KasaGatewayStatusResponse GetStatus()
    {
        var configuredDevices = registry.GetEnabledDevicesAsync().GetAwaiter().GetResult();
        var devices = BuildDeviceList(configuredDevices);
        return new KasaGatewayStatusResponse(
            options.Value.GatewayId,
            options.Value.DisplayTimeZoneId,
            configuredDevices.Count,
            devices.Count(device => device.IsOnline),
            devices.Count(device => device.IsDegraded),
            LastPollStartedAtUtc,
            LastPollCompletedAtUtc,
            LastError,
            devices);
    }

    public KasaGatewayHealthResponse GetHealth()
    {
        var status = GetStatus();
        return new KasaGatewayHealthResponse(
            status.GatewayId,
            status.ConfiguredDeviceCount,
            status.OnlineDeviceCount,
            status.DegradedDeviceCount,
            status.LastPollStartedAtUtc,
            status.LastPollCompletedAtUtc,
            status.LastError);
    }

    public KasaGatewayReviewStatusResponse GetReviewStatus()
    {
        var status = GetStatus();
        return new KasaGatewayReviewStatusResponse(
            status.GatewayId,
            status.ConfiguredDeviceCount,
            status.OnlineDeviceCount,
            status.DegradedDeviceCount,
            status.LastPollStartedAtUtc,
            status.LastPollCompletedAtUtc,
            status.LastError,
            status.Devices.Select(KasaReviewDeviceStatus.FromStatus).ToArray());
    }

    public KasaGatewayReviewCurrentStatusResponse GetReviewCurrentStatus()
    {
        var status = GetStatus();
        return new KasaGatewayReviewCurrentStatusResponse(
            status.GatewayId,
            status.ConfiguredDeviceCount,
            status.OnlineDeviceCount,
            status.DegradedDeviceCount,
            status.LastPollStartedAtUtc,
            status.LastPollCompletedAtUtc,
            status.LastError,
            status.Devices.Select(KasaReviewCurrentDeviceStatus.FromStatus).ToArray());
    }

    public async Task<KasaGatewayInventoryResponse> GetInventoryAsync(CancellationToken cancellationToken = default) => new(
        options.Value.GatewayId,
        (await registry.GetEnabledDevicesAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(device => device.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(KasaInventoryDevice.FromConfig)
            .ToArray());

    public KasaGatewayInventoryResponse GetInventory() => GetInventoryAsync().GetAwaiter().GetResult();

    public KasaDeviceListResponse GetDevices() => new(
        options.Value.GatewayId,
        GetStatus().Devices);

    public async Task<KasaDeviceListResponse> GetDevicesAsync(CancellationToken cancellationToken = default) => new(
        options.Value.GatewayId,
        (await GetStatusAsync(cancellationToken).ConfigureAwait(false)).Devices);

    public KasaDeviceStatus? GetDeviceBySourceId(string sourceId) =>
        string.IsNullOrWhiteSpace(sourceId)
            ? null
            : GetStatus().Devices.FirstOrDefault(device => string.Equals(device.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

    public async Task<KasaDeviceStatus?> GetDeviceBySourceIdAsync(string sourceId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(sourceId)
            ? null
            : (await GetStatusAsync(cancellationToken).ConfigureAwait(false)).Devices.FirstOrDefault(device => string.Equals(device.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

    public KasaDeviceSearchResponse SearchDevices(KasaDeviceSearchRequest request)
    {
        var devices = ApplySearch(GetStatus().Devices, request);
        var matches = devices.OrderBy(device => device.SourceId ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToArray();
        return new KasaDeviceSearchResponse(options.Value.GatewayId, matches.Length, matches);
    }

    public async Task<KasaDeviceSearchResponse> SearchDevicesAsync(KasaDeviceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var devices = ApplySearch(status.Devices, request);
        var matches = devices.OrderBy(device => device.SourceId ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToArray();
        return new KasaDeviceSearchResponse(options.Value.GatewayId, matches.Length, matches);
    }

    private IReadOnlyList<KasaDeviceStatus> BuildDeviceList(IReadOnlyList<KasaDeviceConfig> configuredDevices) =>
        configuredDevices
            .Select(device => _devices.TryGetValue(device.EffectiveSourceId, out var status)
                ? status
                : KasaDeviceStatus.Offline(device, "Waiting for first poll."))
            .OrderBy(device => device.DeviceKind.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DisplayName ?? device.SourceId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<KasaDeviceStatus> ApplySearch(IEnumerable<KasaDeviceStatus> devices, KasaDeviceSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            devices = devices.Where(device => Contains(device.SourceId, request.Query) || Contains(device.Model, request.Query) || Contains(device.DisplayName, request.Query) || Contains(device.Host, request.Query) || Contains(device.MacAddress, request.Query));
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            devices = devices.Where(device => Contains(device.Model, request.Model));
        }

        if (!string.IsNullOrWhiteSpace(request.Kind))
        {
            devices = devices.Where(device => string.Equals(device.DeviceKind.ToString(), request.Kind, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Capability))
        {
            devices = devices.Where(device => device.Capabilities.Any(capability => string.Equals(capability.ToString(), request.Capability, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(request.MetadataCapability))
        {
            devices = devices.Where(device => device.MetadataCapabilities.Any(capability => string.Equals(capability.ToString(), request.MetadataCapability, StringComparison.OrdinalIgnoreCase)));
        }

        if (request.Online is not null)
        {
            devices = devices.Where(device => device.IsOnline == request.Online.Value);
        }

        if (request.Degraded is not null)
        {
            devices = devices.Where(device => device.IsDegraded == request.Degraded.Value);
        }

        return devices;
    }

    private static bool Contains(string? value, string expected) =>
        value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;
}

public sealed record KasaGatewayStatusResponse(
    string GatewayId,
    string DisplayTimeZoneId,
    int ConfiguredDeviceCount,
    int OnlineDeviceCount,
    int DegradedDeviceCount,
    DateTimeOffset? LastPollStartedAtUtc,
    DateTimeOffset? LastPollCompletedAtUtc,
    string? LastError,
    IReadOnlyList<KasaDeviceStatus> Devices);

public sealed record KasaGatewayInventoryResponse(
    string GatewayId,
    IReadOnlyList<KasaInventoryDevice> Devices);

public sealed record KasaDeviceListResponse(
    string GatewayId,
    IReadOnlyList<KasaDeviceStatus> Devices);

public sealed record KasaDeviceSearchRequest(
    string? Query,
    string? Model,
    string? Kind,
    string? Capability,
    string? MetadataCapability,
    bool? Online,
    bool? Degraded);

public sealed record KasaDeviceSearchResponse(
    string GatewayId,
    int MatchCount,
    IReadOnlyList<KasaDeviceStatus> Devices);

public sealed record KasaGatewayHealthResponse(
    string GatewayId,
    int ConfiguredDeviceCount,
    int OnlineDeviceCount,
    int DegradedDeviceCount,
    DateTimeOffset? LastPollStartedAtUtc,
    DateTimeOffset? LastPollCompletedAtUtc,
    string? LastError);

public sealed record KasaGatewayReviewStatusResponse(
    string GatewayId,
    int ConfiguredDeviceCount,
    int OnlineDeviceCount,
    int DegradedDeviceCount,
    DateTimeOffset? LastPollStartedAtUtc,
    DateTimeOffset? LastPollCompletedAtUtc,
    string? LastError,
    IReadOnlyList<KasaReviewDeviceStatus> Devices);

public sealed record KasaGatewayReviewCurrentStatusResponse(
    string GatewayId,
    int ConfiguredDeviceCount,
    int OnlineDeviceCount,
    int DegradedDeviceCount,
    DateTimeOffset? LastPollStartedAtUtc,
    DateTimeOffset? LastPollCompletedAtUtc,
    string? LastError,
    IReadOnlyList<KasaReviewCurrentDeviceStatus> Devices);

public sealed record KasaReviewDeviceStatus(
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    string DeviceKind,
    bool IsOnline,
    bool IdentityValidated,
    bool IsDegraded,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> MetadataCapabilities,
    IReadOnlyList<string> CommandCapabilities,
    int OutletCount,
    bool HasLightStatus,
    bool HasEnergyStatus,
    KasaReadModuleSupport? ReadSupport)
{
    public static KasaReviewDeviceStatus FromStatus(KasaDeviceStatus status) => new(
        status.Model,
        status.HardwareVersion,
        status.SoftwareVersion,
        status.DeviceKind.ToString(),
        status.IsOnline,
        status.IdentityValidated,
        status.IsDegraded,
        status.Capabilities.Select(capability => capability.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        status.MetadataCapabilities.Select(capability => capability.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        status.CommandCapabilities.Select(capability => capability.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        status.Outlets.Count,
        status.Light is not null,
        status.Energy is not null,
        status.ReadMetadata?.Support);
}

public sealed record KasaReviewCurrentDeviceStatus(
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    string DeviceKind,
    DateTimeOffset? ObservedAtUtc,
    string? DisplayTimeZoneId,
    bool IsOnline,
    bool IdentityValidated,
    bool IsDegraded,
    string? FailureReason,
    string? DegradedReason,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> MetadataCapabilities,
    IReadOnlyList<string> CommandCapabilities,
    bool? IsOn,
    IReadOnlyList<KasaOutletStatus> Outlets,
    KasaLightStatus? Light,
    KasaEnergyStatus? Energy,
    KasaDeviceInfo? DeviceInfo,
    KasaReadMetadataSnapshot? ReadMetadata)
{
    public static KasaReviewCurrentDeviceStatus FromStatus(KasaDeviceStatus status) => new(
        status.Model,
        status.HardwareVersion,
        status.SoftwareVersion,
        status.DeviceKind.ToString(),
        status.ObservedAtUtc,
        status.DisplayTimeZoneId,
        status.IsOnline,
        status.IdentityValidated,
        status.IsDegraded,
        status.FailureReason,
        status.DegradedReason,
        status.Capabilities.Select(capability => capability.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        status.MetadataCapabilities.Select(capability => capability.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        status.CommandCapabilities.Select(capability => capability.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        status.IsOn,
        status.Outlets,
        status.Light,
        status.Energy,
        status.DeviceInfo,
        status.ReadMetadata);
}

public sealed record KasaInventoryDevice(
    bool DeviceIdConfigured,
    string? SourceId,
    bool HostConfigured,
    int? Port,
    string? NetworkName,
    string? DisplayTimeZoneId,
    string? ExpectedModel,
    string? ExpectedHardwareVersion,
    string? ExpectedSoftwareVersion,
    int? ExpectedChildCount,
    KasaProtocolFamily ProtocolFamily,
    KasaDeviceKind DeviceKind,
    IReadOnlyList<KasaCapability> Capabilities,
    IReadOnlyList<KasaMetadataCapability> MetadataCapabilities,
    IReadOnlyList<KasaCommandCapability> CommandCapabilities,
    KasaSafetyClass SafetyClass)
{
    public static KasaInventoryDevice FromConfig(KasaDeviceConfig config) => new(
        !string.IsNullOrWhiteSpace(config.DeviceId),
        PublicSourceId(config),
        !string.IsNullOrWhiteSpace(config.Host),
        config.Port,
        config.NetworkName,
        config.DisplayTimeZoneId,
        config.ExpectedModel,
        config.ExpectedHardwareVersion,
        config.ExpectedSoftwareVersion,
        config.ExpectedChildCount,
        config.ProtocolFamily,
        config.DeviceKind,
        config.Capabilities,
        config.MetadataCapabilities,
        config.CommandCapabilities,
        config.SafetyClass);

    internal static string? PublicSourceId(KasaDeviceConfig config) =>
        string.IsNullOrWhiteSpace(config.SourceId) ? null : config.SourceId;
}

public sealed record KasaDeviceStatus(
    bool DeviceIdConfigured,
    string? SourceId,
    string? DisplayName,
    string? Host,
    string? MacAddress,
    bool HostConfigured,
    DateTimeOffset? ObservedAtUtc,
    string? DisplayTimeZoneId,
    bool IsOnline,
    bool IdentityValidated,
    bool IsDegraded,
    string? FailureReason,
    string? DegradedReason,
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    KasaDeviceKind DeviceKind,
    IReadOnlySet<KasaCapability> Capabilities,
    IReadOnlySet<KasaMetadataCapability> MetadataCapabilities,
    IReadOnlySet<KasaCommandCapability> CommandCapabilities,
    bool? IsOn,
    IReadOnlyList<KasaOutletStatus> Outlets,
    KasaLightStatus? Light,
    KasaEnergyStatus? Energy,
    KasaDeviceInfo? DeviceInfo,
    KasaReadMetadataSnapshot? ReadMetadata)
{
    public static KasaDeviceStatus Online(KasaDeviceConfig config, KasaDeviceSnapshot snapshot, string? degradedReason) => new(
        !string.IsNullOrWhiteSpace(config.DeviceId),
        KasaInventoryDevice.PublicSourceId(config),
        snapshot.Alias ?? config.DisplayName,
        snapshot.Host,
        snapshot.MacAddress,
        !string.IsNullOrWhiteSpace(config.Host),
        snapshot.ObservedAtUtc,
        config.DisplayTimeZoneId,
        true,
        snapshot.IdentityValidated,
        degradedReason is not null,
        null,
        degradedReason,
        snapshot.Model,
        snapshot.HardwareVersion,
        snapshot.SoftwareVersion,
        snapshot.DeviceKind,
        snapshot.Capabilities,
        snapshot.MetadataCapabilities,
        snapshot.CommandCapabilities,
        snapshot.IsOn,
        snapshot.Outlets.Select(KasaOutletStatus.FromSnapshot).ToArray(),
        KasaLightStatus.FromSnapshot(snapshot.Light),
        KasaEnergyStatus.FromReading(snapshot.Energy),
        snapshot.DeviceInfo,
        snapshot.ReadMetadata);

    public static KasaDeviceStatus Offline(KasaDeviceConfig config, string failureReason) => new(
        !string.IsNullOrWhiteSpace(config.DeviceId),
        KasaInventoryDevice.PublicSourceId(config),
        config.DisplayName,
        config.Host,
        config.MacAddress,
        !string.IsNullOrWhiteSpace(config.Host),
        null,
        config.DisplayTimeZoneId,
        false,
        false,
        false,
        failureReason,
        null,
        config.ExpectedModel,
        config.ExpectedHardwareVersion,
        config.ExpectedSoftwareVersion,
        config.DeviceKind,
        config.Capabilities.ToHashSet(),
        config.MetadataCapabilities.ToHashSet(),
        config.CommandCapabilities.ToHashSet(),
        null,
        [],
        null,
        null,
        new KasaDeviceInfo(
            null,
            config.ExpectedModel,
            config.ExpectedHardwareVersion,
            config.ExpectedSoftwareVersion,
            config.MacAddress,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null),
        null);
}

public sealed record KasaOutletStatus(int? Index, string? DisplayName, bool? IsOn, int? OnTimeSeconds, KasaEnergyStatus? Energy)
{
    public static KasaOutletStatus FromSnapshot(KasaOutletSnapshot snapshot) => new(snapshot.Index, snapshot.Alias, snapshot.IsOn, snapshot.OnTimeSeconds, KasaEnergyStatus.FromReading(snapshot.Energy));
}

public sealed record KasaLightStatus(
    bool? IsOn,
    int? Brightness,
    int? Hue,
    int? Saturation,
    int? ColorTemperature,
    string? Mode,
    IReadOnlyList<KasaPreferredLightState> PreferredStates,
    KasaBulbLightDetailsMetadata? BulbDetails,
    KasaBulbDefaultBehaviorMetadata? DefaultBehavior)
{
    public static KasaLightStatus? FromSnapshot(KasaLightSnapshot? snapshot) => snapshot is null
        ? null
        : new KasaLightStatus(snapshot.IsOn, snapshot.Brightness, snapshot.Hue, snapshot.Saturation, snapshot.ColorTemperature, snapshot.Mode, snapshot.PreferredStates, snapshot.BulbDetails, snapshot.DefaultBehavior);
}

public sealed record KasaEnergyStatus(double? PowerW, double? VoltageV, double? CurrentA, double? EnergyKWh)
{
    public static KasaEnergyStatus? FromReading(KasaEnergyReading? reading) => reading is null
        ? null
        : new KasaEnergyStatus(reading.PowerW, reading.VoltageV, reading.CurrentA, reading.EnergyKWh);
}

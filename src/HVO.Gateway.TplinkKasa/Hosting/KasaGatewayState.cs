using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HVO.Gateway.TplinkKasa.Hosting;

public sealed class KasaGatewayState(IOptions<KasaGatewayOptions> options)
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

        _devices[config.EffectiveSourceId] = status;
    }

    public KasaGatewayStatusResponse GetStatus()
    {
        var configuredDevices = options.Value.Devices.Where(device => device.Enabled).ToArray();
        var devices = _devices.Values.OrderBy(device => device.SourceId ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToArray();
        return new KasaGatewayStatusResponse(
            options.Value.GatewayId,
            configuredDevices.Length,
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

    public KasaGatewayInventoryResponse GetInventory() => new(
        options.Value.GatewayId,
        options.Value.Devices
            .Where(device => device.Enabled)
            .OrderBy(device => device.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(KasaInventoryDevice.FromConfig)
            .ToArray());
}

public sealed record KasaGatewayStatusResponse(
    string GatewayId,
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

public sealed record KasaReviewDeviceStatus(
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    KasaDeviceKind DeviceKind,
    bool IsOnline,
    bool IdentityValidated,
    bool IsDegraded,
    IReadOnlySet<KasaCapability> Capabilities,
    IReadOnlySet<KasaMetadataCapability> MetadataCapabilities,
    int OutletCount,
    bool HasLightStatus,
    bool HasEnergyStatus,
    KasaReadModuleSupport? ReadSupport)
{
    public static KasaReviewDeviceStatus FromStatus(KasaDeviceStatus status) => new(
        status.Model,
        status.HardwareVersion,
        status.SoftwareVersion,
        status.DeviceKind,
        status.IsOnline,
        status.IdentityValidated,
        status.IsDegraded,
        status.Capabilities,
        status.MetadataCapabilities,
        status.Outlets.Count,
        status.Light is not null,
        status.Energy is not null,
        status.ReadMetadata?.Support);
}

public sealed record KasaInventoryDevice(
    bool DeviceIdConfigured,
    string? SourceId,
    bool HostConfigured,
    int? Port,
    string? NetworkName,
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
    bool HostConfigured,
    DateTimeOffset? ObservedAtUtc,
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
    bool? IsOn,
    IReadOnlyList<KasaOutletStatus> Outlets,
    KasaLightStatus? Light,
    KasaEnergyStatus? Energy,
    KasaReadMetadataSnapshot? ReadMetadata)
{
    public static KasaDeviceStatus Online(KasaDeviceConfig config, KasaDeviceSnapshot snapshot, string? degradedReason) => new(
        !string.IsNullOrWhiteSpace(config.DeviceId),
        KasaInventoryDevice.PublicSourceId(config),
        !string.IsNullOrWhiteSpace(config.Host),
        snapshot.ObservedAtUtc,
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
        snapshot.IsOn,
        snapshot.Outlets.Select(KasaOutletStatus.FromSnapshot).ToArray(),
        KasaLightStatus.FromSnapshot(snapshot.Light),
        KasaEnergyStatus.FromReading(snapshot.Energy),
        snapshot.ReadMetadata);

    public static KasaDeviceStatus Offline(KasaDeviceConfig config, string failureReason) => new(
        !string.IsNullOrWhiteSpace(config.DeviceId),
        KasaInventoryDevice.PublicSourceId(config),
        !string.IsNullOrWhiteSpace(config.Host),
        null,
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
        null,
        [],
        null,
        null,
        null);
}

public sealed record KasaOutletStatus(int? Index, bool? IsOn, int? OnTimeSeconds)
{
    public static KasaOutletStatus FromSnapshot(KasaOutletSnapshot snapshot) => new(snapshot.Index, snapshot.IsOn, snapshot.OnTimeSeconds);
}

public sealed record KasaLightStatus(
    bool? IsOn,
    int? Brightness,
    int? Hue,
    int? Saturation,
    int? ColorTemperature,
    string? Mode)
{
    public static KasaLightStatus? FromSnapshot(KasaLightSnapshot? snapshot) => snapshot is null
        ? null
        : new KasaLightStatus(snapshot.IsOn, snapshot.Brightness, snapshot.Hue, snapshot.Saturation, snapshot.ColorTemperature, snapshot.Mode);
}

public sealed record KasaEnergyStatus(double? PowerW, double? VoltageV, double? CurrentA, double? EnergyKWh)
{
    public static KasaEnergyStatus? FromReading(KasaEnergyReading? reading) => reading is null
        ? null
        : new KasaEnergyStatus(reading.PowerW, reading.VoltageV, reading.CurrentA, reading.EnergyKWh);
}

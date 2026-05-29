using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed record KasaSystemInfo(
    string? DeviceId,
    string? Alias,
    string? Model,
    string? DeviceType,
    string? HardwareVersion,
    string? SoftwareVersion,
    string? MacAddress,
    int? RelayState,
    int? OnTimeSeconds,
    IReadOnlyList<KasaChildInfo> Children,
    KasaLightState? LightState,
    JsonElement RawSystemInfo);

public sealed record KasaChildInfo(
    string? Id,
    string? Alias,
    int? State,
    int? OnTimeSeconds);

public sealed record KasaLightState(
    int? IsOn,
    int? Hue,
    int? Saturation,
    int? ColorTemperature,
    int? Brightness,
    string? Mode,
    JsonElement RawLightState);

public sealed record KasaEnergyReading(
    double? PowerW,
    double? VoltageV,
    double? CurrentA,
    double? EnergyKWh,
    JsonElement RawRealtimeEnergy);

public sealed record KasaDeviceProfile(
    KasaDeviceKind DeviceKind,
    IReadOnlySet<KasaCapability> Capabilities,
    IReadOnlySet<KasaMetadataCapability> MetadataCapabilities,
    IReadOnlySet<KasaCommandCapability> CommandCapabilities);

public sealed record KasaIdentityValidationResult(
    bool IsValid,
    string? Reason,
    bool DeviceIdMatched,
    bool MacMatched,
    bool ModelMatched,
    bool ChildCountMatched);

public sealed record KasaDeviceSnapshot(
    string DeviceId,
    string SourceId,
    string Host,
    DateTimeOffset ObservedAtUtc,
    bool IsOnline,
    bool IdentityValidated,
    string? IdentityMismatchReason,
    string? Alias,
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    string? MacAddress,
    KasaDeviceKind DeviceKind,
    IReadOnlySet<KasaCapability> Capabilities,
    IReadOnlySet<KasaMetadataCapability> MetadataCapabilities,
    bool? IsOn,
    IReadOnlyList<KasaOutletSnapshot> Outlets,
    KasaLightSnapshot? Light,
    KasaEnergyReading? Energy,
    JsonElement RawSystemInfo);

public sealed record KasaOutletSnapshot(
    string OutletId,
    int? Index,
    string? Alias,
    bool? IsOn,
    int? OnTimeSeconds);

public sealed record KasaLightSnapshot(
    bool? IsOn,
    int? Brightness,
    int? Hue,
    int? Saturation,
    int? ColorTemperature,
    string? Mode,
    JsonElement RawLightState);

public sealed record KasaPollResult(KasaDeviceSnapshot? Snapshot, string? FailureReason)
{
    public bool IsSuccess => Snapshot is not null && FailureReason is null;
}

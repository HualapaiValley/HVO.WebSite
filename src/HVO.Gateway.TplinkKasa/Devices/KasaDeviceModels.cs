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
    string? HardwareId,
    string? FirmwareId,
    string? OemId,
    string? Feature,
    string? ActiveMode,
    int? Rssi,
    KasaDeviceLocation? Location,
    int? RelayState,
    int? OnTimeSeconds,
    IReadOnlyList<KasaChildInfo> Children,
    KasaLightState? LightState,
    IReadOnlyList<KasaPreferredLightState> PreferredLightStates,
    JsonElement RawSystemInfo);

public sealed record KasaDeviceLocation(int? LatitudeRaw, int? LongitudeRaw, double? LatitudeDegrees, double? LongitudeDegrees);

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

public sealed record KasaPreferredLightState(
    int? Index,
    int? Brightness,
    int? Hue,
    int? Saturation,
    int? ColorTemperature);

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
    IReadOnlySet<KasaCommandCapability> CommandCapabilities,
    bool? IsOn,
    IReadOnlyList<KasaOutletSnapshot> Outlets,
    KasaLightSnapshot? Light,
    KasaEnergyReading? Energy,
    KasaDeviceInfo? DeviceInfo,
    KasaReadMetadataSnapshot? ReadMetadata,
    JsonElement RawSystemInfo);

public sealed record KasaDeviceInfo(
    string? DeviceType,
    string? Model,
    string? HardwareVersion,
    string? SoftwareVersion,
    string? MacAddress,
    string? HardwareId,
    string? FirmwareId,
    string? OemId,
    string? Feature,
    string? ActiveMode,
    int? Rssi,
    KasaDeviceLocation? Location,
    KasaDeviceTimeMetadata? DeviceTime,
    KasaTimezoneMetadata? Timezone,
    int? DeviceUtcOffsetMinutes,
    string? DeviceTimeZoneLabel,
    KasaCloudMetadata? Cloud,
    KasaFirmwareDownloadMetadata? FirmwareDownload,
    KasaFirmwareListMetadata? CloudFirmware,
    KasaDimmerMetadata? Dimmer);

public sealed record KasaReadMetadataSnapshot(
    KasaRuleMetadata? Schedule,
    KasaNextActionMetadata? ScheduleNextAction,
    KasaRuleMetadata? Countdown,
    KasaRuleMetadata? Away,
    KasaDeviceTimeMetadata? DeviceTime,
    KasaTimezoneMetadata? Timezone,
    KasaFirmwareDownloadMetadata? FirmwareDownload,
    KasaCloudMetadata? Cloud,
    KasaFirmwareListMetadata? CloudFirmware,
    KasaBulbLightDetailsMetadata? BulbLightDetails,
    KasaBulbDefaultBehaviorMetadata? BulbDefaultBehavior,
    KasaDimmerMetadata? Dimmer,
    KasaReadModuleSupport Support);

public sealed record KasaRuleMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, bool? Enabled, int? Version, int? RuleCount);

public sealed record KasaNextActionMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, int? Type);

public sealed record KasaDeviceTimeMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, int? Year, int? Month, int? Day, int? Hour, int? Minute, int? Second);

public sealed record KasaTimezoneMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, int? Index);

public sealed record KasaFirmwareDownloadMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, int? Status, int? Ratio, int? FlashTimeSeconds, int? RebootTimeSeconds);

public sealed record KasaCloudMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, bool? IsBound, bool? IsConnected, int? FirmwareNotifyType, int? IllegalType, bool? StopConnect, int? TcspStatus);

public sealed record KasaFirmwareListMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, int? FirmwareCount);

public sealed record KasaBulbLightDetailsMetadata(
    bool IsSupported,
    int? ErrorCode,
    string? ErrorMessage,
    int? Wattage,
    int? MaxLumens,
    int? ColorRenderingIndex,
    int? IncandescentEquivalent,
    int? LampBeamAngle,
    int? MinVoltage,
    int? MaxVoltage);

public sealed record KasaBulbDefaultBehaviorMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, string? SoftOnMode, string? HardOnMode);

public sealed record KasaDimmerMetadata(KasaDimmerDefaultBehaviorMetadata? DefaultBehavior, KasaDimmerParameterMetadata? Parameters);

public sealed record KasaDimmerDefaultBehaviorMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, string? SoftOnMode, string? HardOnMode, string? DoubleClickMode, string? LongPressMode);

public sealed record KasaDimmerParameterMetadata(bool IsSupported, int? ErrorCode, string? ErrorMessage, int? BulbType, int? FadeOnTimeMs, int? FadeOffTimeMs, int? GentleOnTimeMs, int? GentleOffTimeMs, int? MinThreshold, int? RampRate);

public sealed record KasaReadModuleSupport(
    bool EnergyRealtime,
    bool ScheduleRules,
    bool ScheduleNextAction,
    bool CountdownRules,
    bool AwayRules,
    bool DeviceTime,
    bool Timezone,
    bool FirmwareDownload,
    bool CloudInfo,
    bool CloudFirmwareList,
    bool BulbLightDetails,
    bool BulbDefaultBehavior,
    bool DimmerDefaultBehavior,
    bool DimmerParameters);

public sealed record KasaOutletSnapshot(
    string OutletId,
    int? Index,
    string? Alias,
    bool? IsOn,
    int? OnTimeSeconds,
    KasaEnergyReading? Energy);

public sealed record KasaLightSnapshot(
    bool? IsOn,
    int? Brightness,
    int? Hue,
    int? Saturation,
    int? ColorTemperature,
    string? Mode,
    IReadOnlyList<KasaPreferredLightState> PreferredStates,
    KasaBulbLightDetailsMetadata? BulbDetails,
    KasaBulbDefaultBehaviorMetadata? DefaultBehavior,
    JsonElement RawLightState);

public sealed record KasaPollResult(KasaDeviceSnapshot? Snapshot, string? FailureReason, string? DegradedReason = null)
{
    public bool IsSuccess => Snapshot is not null && FailureReason is null;

    public bool IsDegraded => IsSuccess && DegradedReason is not null;
}

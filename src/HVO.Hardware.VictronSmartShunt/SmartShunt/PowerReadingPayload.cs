using System.Text.Json.Serialization;

namespace HVO.Hardware.VictronSmartShunt.SmartShunt;

public sealed class PowerReadingPayload
{
    [JsonPropertyName("sourceId")]
    public string? SourceId { get; init; }

    [JsonPropertyName("sourceSystem")]
    public string? SourceSystem { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("recordedAtUtc")]
    public DateTime RecordedAtUtc { get; init; }

    [JsonPropertyName("pvPowerW")]
    public double? PvPowerW { get; init; }

    [JsonPropertyName("loadPowerW")]
    public double? LoadPowerW { get; init; }

    [JsonPropertyName("gridPowerW")]
    public double? GridPowerW { get; init; }

    [JsonPropertyName("batteryPowerW")]
    public double? BatteryPowerW { get; init; }

    [JsonPropertyName("systemPowerW")]
    public double? SystemPowerW { get; init; }

    [JsonPropertyName("batteryStateOfChargePercent")]
    public double? BatteryStateOfChargePercent { get; init; }

    [JsonPropertyName("batteryVoltageV")]
    public double? BatteryVoltageV { get; init; }

    [JsonPropertyName("batteryCurrentA")]
    public double? BatteryCurrentA { get; init; }

    [JsonPropertyName("batteryCapacityKwh")]
    public double? BatteryCapacityKwh { get; init; }

    [JsonPropertyName("gridVoltageV")]
    public double? GridVoltageV { get; init; }

    [JsonPropertyName("gridFrequencyHz")]
    public double? GridFrequencyHz { get; init; }

    [JsonPropertyName("outputVoltageV")]
    public double? OutputVoltageV { get; init; }

    [JsonPropertyName("outputFrequencyHz")]
    public double? OutputFrequencyHz { get; init; }

    [JsonPropertyName("loadPercentage")]
    public double? LoadPercentage { get; init; }

    [JsonPropertyName("inverterMode")]
    public string? InverterMode { get; init; }

    [JsonPropertyName("outputSourcePriority")]
    public string? OutputSourcePriority { get; init; }

    [JsonPropertyName("chargerSourcePriority")]
    public string? ChargerSourcePriority { get; init; }
}

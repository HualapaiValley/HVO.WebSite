using System.ComponentModel.DataAnnotations;

namespace HVO.Edge.Contracts.Weather;

public sealed record DavisWeatherArchivePayload
{
    [Required, MaxLength(64)]
    public required string StationId { get; init; }
    public required DateTime RecordedAtUtc { get; init; }
    public required DateTime ConsoleRecordedAtLocal { get; init; }
    [Range(1, 1440)] public int ArchiveIntervalMinutes { get; init; }
    public double? TemperatureF { get; init; }
    public double? HighTemperatureF { get; init; }
    public double? LowTemperatureF { get; init; }
    public double? InsideTemperatureF { get; init; }
    public double? HumidityPercent { get; init; }
    public double? InsideHumidityPercent { get; init; }
    public double? BarometricPressureInHg { get; init; }
    public double? WindSpeedMph { get; init; }
    public double? WindGustMph { get; init; }
    public double? WindDirectionDegrees { get; init; }
    public double? WindGustDirectionDegrees { get; init; }
    public int WindSamples { get; init; }
    public double? RainfallInches { get; init; }
    public double? RainRateInchesPerHour { get; init; }
    public double? SolarRadiationWm2 { get; init; }
    public double? HighSolarRadiationWm2 { get; init; }
    public double? UvIndex { get; init; }
    public double? HighUvIndex { get; init; }
    public double? EtInches { get; init; }
    public int? ForecastRule { get; init; }
    [MaxLength(512)] public string? ForecastString { get; init; }
    public int DownloadRecordType { get; init; }
    public double? LeafTemp1F { get; init; }
    public double? LeafTemp2F { get; init; }
    public IReadOnlyList<double?> LeafWetnessScaled { get; init; } = [];
    public IReadOnlyList<double?> SoilTemperaturesF { get; init; } = [];
    public IReadOnlyList<double?> ExtraHumiditiesPercent { get; init; } = [];
    public IReadOnlyList<double?> ExtraTemperaturesF { get; init; } = [];
    public IReadOnlyList<double?> SoilMoisturesCb { get; init; } = [];
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

[Table("WeatherArchive", Schema = "v9")]
public sealed class WeatherArchive
{
    [Key] public long Id { get; set; }
    [MaxLength(64)] public string StationId { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public DateTime ConsoleRecordedAtLocal { get; set; }
    public int ArchiveIntervalMinutes { get; set; }
    public double? TemperatureF { get; set; }
    public double? HighTemperatureF { get; set; }
    public double? LowTemperatureF { get; set; }
    public double? InsideTemperatureF { get; set; }
    public double? HumidityPercent { get; set; }
    public double? InsideHumidityPercent { get; set; }
    public double? BarometricPressureInHg { get; set; }
    public double? WindSpeedMph { get; set; }
    public double? WindGustMph { get; set; }
    public double? WindDirectionDegrees { get; set; }
    public double? WindGustDirectionDegrees { get; set; }
    public int WindSamples { get; set; }
    public double? RainfallInches { get; set; }
    public double? RainRateInchesPerHour { get; set; }
    public double? SolarRadiationWm2 { get; set; }
    public double? HighSolarRadiationWm2 { get; set; }
    public double? UvIndex { get; set; }
    public double? HighUvIndex { get; set; }
    public double? EtInches { get; set; }
    public int? ForecastRule { get; set; }
    [MaxLength(512)] public string? ForecastString { get; set; }
    public int DownloadRecordType { get; set; }
    public double? LeafTemp1F { get; set; }
    public double? LeafTemp2F { get; set; }
    public string LeafWetnessJson { get; set; } = "[]";
    public string SoilTemperaturesJson { get; set; } = "[]";
    public string ExtraHumiditiesJson { get; set; } = "[]";
    public string ExtraTemperaturesJson { get; set; } = "[]";
    public string SoilMoisturesJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

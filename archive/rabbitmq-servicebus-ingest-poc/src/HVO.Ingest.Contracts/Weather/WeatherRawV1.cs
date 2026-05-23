using System.ComponentModel.DataAnnotations;

namespace HVO.Ingest.Contracts.Weather;

public sealed record WeatherRawV1
{
    [Required]
    [MaxLength(64)]
    public string StationId { get; init; } = string.Empty;

    public DateTimeOffset? RecordedAt { get; init; }

    public int? ArchiveIntervalMinutes { get; init; }

    public double? TemperatureF { get; init; }
    public double? HighTemperatureF { get; init; }
    public double? LowTemperatureF { get; init; }
    public double? InsideTemperatureF { get; init; }
    public double? DewPointF { get; init; }
    public double? HeatIndexF { get; init; }
    public double? WindChillF { get; init; }
    public double? ThswF { get; init; }

    [Range(0, 100)]
    public double? HumidityPercent { get; init; }

    [Range(0, 100)]
    public double? InsideHumidityPercent { get; init; }

    [Range(20, 35)]
    public double? BarometricPressureInHg { get; init; }

    public double? PressureRawInHg { get; init; }
    public double? AltimeterInHg { get; init; }
    public int? BarometricTrend { get; init; }

    [Range(0, 300)]
    public double? WindSpeedMph { get; init; }

    [Range(0, 300)]
    public double? WindGustMph { get; init; }

    [Range(0, 359)]
    public int? WindDirectionDegrees { get; init; }

    [Range(0, 300)]
    public double? WindSpeed10MinAvgMph { get; init; }

    [Range(0, 300)]
    public double? WindSpeed2MinAvgMph { get; init; }

    [Range(0, 300)]
    public double? WindGust10MinMph { get; init; }

    [Range(0, 359)]
    public int? WindGust10MinDirectionDegrees { get; init; }

    [Range(0, 359)]
    public int? WindGustDirectionDegrees { get; init; }

    public int? WindSamples { get; init; }

    [Range(0, 100)]
    public double? RainfallInches { get; init; }

    public double? RainRateInchesPerHour { get; init; }
    public double? DailyRainInches { get; init; }
    public double? Rain15MinInches { get; init; }
    public double? HourRainInches { get; init; }
    public double? Rain24HourInches { get; init; }
    public double? StormRainInches { get; init; }
    public DateTimeOffset? StormStartDate { get; init; }
    public double? MonthlyRainInches { get; init; }
    public double? YearlyRainInches { get; init; }

    [Range(0, 2000)]
    public double? SolarRadiationWm2 { get; init; }

    public double? HighSolarRadiationWm2 { get; init; }

    [Range(0, 20)]
    public double? UvIndex { get; init; }

    public double? HighUvIndex { get; init; }
    public double? DailyEtInches { get; init; }
    public double? MonthlyEtInches { get; init; }
    public double? YearlyEtInches { get; init; }
    public double? EtInches { get; init; }

    public double? ConsoleBatteryVoltage { get; init; }
    public ushort? TransmitterBatteryStatus { get; init; }
    public int? ForecastRule { get; init; }
    public string? ForecastString { get; init; }
    public string? SunriseTime { get; init; }
    public string? SunsetTime { get; init; }

    public double? LeafTemp1F { get; init; }
    public double? LeafTemp2F { get; init; }
    public double?[]? LeafWetnessScaled { get; init; }
    public double?[]? SoilTemperaturesF { get; init; }
    public double?[]? ExtraHumiditiesPercent { get; init; }
    public double?[]? ExtraTemperaturesF { get; init; }
    public double?[]? SoilMoisturesCb { get; init; }
}

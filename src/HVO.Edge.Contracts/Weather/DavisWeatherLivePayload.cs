using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HVO.Edge.Contracts.Weather;

public sealed record DavisWeatherLivePayload
{
    [Required, MaxLength(64)]
    public required string StationId { get; init; }
    [JsonPropertyName("recordedAt")]
    public required DateTime RecordedAtUtc { get; init; }
    public double? TemperatureF { get; init; }
    public double? InsideTemperatureF { get; init; }
    public double? DewPointF { get; init; }
    public double? HeatIndexF { get; init; }
    public double? WindChillF { get; init; }
    public double? ThswF { get; init; }
    public double? HumidityPercent { get; init; }
    public double? InsideHumidityPercent { get; init; }
    public double? BarometricPressureInHg { get; init; }
    public double? PressureRawInHg { get; init; }
    public double? AltimeterInHg { get; init; }
    public int? BarometricTrend { get; init; }
    public double? WindSpeedMph { get; init; }
    public int? WindDirectionDegrees { get; init; }
    public double? WindSpeed10MinAvgMph { get; init; }
    public double? WindSpeed2MinAvgMph { get; init; }
    public double? WindGust10MinMph { get; init; }
    public int? WindGust10MinDirectionDegrees { get; init; }
    public double? RainRateInchesPerHour { get; init; }
    public double? DailyRainInches { get; init; }
    public double? Rain15MinInches { get; init; }
    public double? HourRainInches { get; init; }
    public double? Rain24HourInches { get; init; }
    public double? StormRainInches { get; init; }
    public DateTime? StormStartDate { get; init; }
    public double? MonthlyRainInches { get; init; }
    public double? YearlyRainInches { get; init; }
    public double? SolarRadiationWm2 { get; init; }
    public double? UvIndex { get; init; }
    public double? DailyEtInches { get; init; }
    public double? MonthlyEtInches { get; init; }
    public double? YearlyEtInches { get; init; }
    public double? ConsoleBatteryVoltage { get; init; }
    public int TransmitterBatteryStatus { get; init; }
    public int? ForecastRule { get; init; }
    public string? ForecastString { get; init; }
    public string? SunriseTime { get; init; }
    public string? SunsetTime { get; init; }
}

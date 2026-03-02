using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Weather data settings from NINA profile
/// </summary>
public record WeatherDataSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("RefreshRate")]
    public double RefreshRate { get; init; }

    [JsonPropertyName("CloudCoverThreshold")]
    public double CloudCoverThreshold { get; init; }

    [JsonPropertyName("DewPointThreshold")]
    public double DewPointThreshold { get; init; }

    [JsonPropertyName("HumidityThreshold")]
    public double HumidityThreshold { get; init; }

    [JsonPropertyName("PressureThreshold")]
    public double PressureThreshold { get; init; }

    [JsonPropertyName("RainRateThreshold")]
    public double RainRateThreshold { get; init; }

    [JsonPropertyName("SkyBrightnessThreshold")]
    public double SkyBrightnessThreshold { get; init; }

    [JsonPropertyName("SkyQualityThreshold")]
    public double SkyQualityThreshold { get; init; }

    [JsonPropertyName("SkyTemperatureThreshold")]
    public double SkyTemperatureThreshold { get; init; }

    [JsonPropertyName("StarFWHMThreshold")]
    public double StarFWHMThreshold { get; init; }

    [JsonPropertyName("TemperatureThreshold")]
    public double TemperatureThreshold { get; init; }

    [JsonPropertyName("WindDirectionThreshold")]
    public double WindDirectionThreshold { get; init; }

    [JsonPropertyName("WindGustThreshold")]
    public double WindGustThreshold { get; init; }

    [JsonPropertyName("WindSpeedThreshold")]
    public double WindSpeedThreshold { get; init; }
}

using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Weather data information
/// </summary>
public record WeatherData
{
    [JsonPropertyName("CloudCover")]
    public double? CloudCover { get; init; }

    [JsonPropertyName("DewPoint")]
    public double? DewPoint { get; init; }

    [JsonPropertyName("Humidity")]
    public double? Humidity { get; init; }

    [JsonPropertyName("Pressure")]
    public double? Pressure { get; init; }

    [JsonPropertyName("RainRate")]
    public double? RainRate { get; init; }

    [JsonPropertyName("SkyBrightness")]
    public double? SkyBrightness { get; init; }

    [JsonPropertyName("SkyQuality")]
    public double? SkyQuality { get; init; }

    [JsonPropertyName("SkyTemperature")]
    public double? SkyTemperature { get; init; }

    [JsonPropertyName("StarFWHM")]
    public double? StarFWHM { get; init; }

    [JsonPropertyName("Temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("WindDirection")]
    public double? WindDirection { get; init; }

    [JsonPropertyName("WindGust")]
    public double? WindGust { get; init; }

    [JsonPropertyName("WindSpeed")]
    public double? WindSpeed { get; init; }
}

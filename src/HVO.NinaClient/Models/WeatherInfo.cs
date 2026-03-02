using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Weather information
/// </summary>
public record WeatherInfo : DeviceInfo
{
    [JsonPropertyName("AveragePeriod")]
    public int AveragePeriod { get; init; }

    [JsonPropertyName("CloudCover")]
    public int CloudCover { get; init; }

    [JsonPropertyName("DewPoint")]
    public double DewPoint { get; init; }

    [JsonPropertyName("Humidity")]
    public int Humidity { get; init; }

    [JsonPropertyName("Pressure")]
    public int Pressure { get; init; }

    [JsonPropertyName("RainRate")]
    public string? RainRate { get; init; }

    [JsonPropertyName("SkyBrightness")]
    public string? SkyBrightness { get; init; }

    [JsonPropertyName("SkyQuality")]
    public string? SkyQuality { get; init; }

    [JsonPropertyName("SkyTemperature")]
    public string? SkyTemperature { get; init; }

    [JsonPropertyName("StarFWHM")]
    public string? StarFWHM { get; init; }

    [JsonPropertyName("Temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("WindDirection")]
    public int WindDirection { get; init; }

    [JsonPropertyName("WindGust")]
    public string? WindGust { get; init; }

    [JsonPropertyName("WindSpeed")]
    public double WindSpeed { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<object>? SupportedActions { get; init; }
}

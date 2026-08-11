using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HVO.Edge.Contracts.Weather;

public sealed class WeatherRawPayload
{
    [Required]
    [MaxLength(64)]
    [JsonPropertyName("stationId")]
    public required string StationId { get; init; }

    [JsonPropertyName("sourceSystem")]
    public string SourceSystem { get; init; } = "homeassistant-govee-ble";

    [JsonPropertyName("recordedAt")]
    public DateTime RecordedAt { get; init; }

    [JsonPropertyName("temperatureF")]
    public double? TemperatureF { get; init; }

    [Range(0, 100)]
    [JsonPropertyName("humidityPercent")]
    public double? HumidityPercent { get; init; }

    [JsonPropertyName("dewPointF")]
    public double? DewPointF { get; init; }
}

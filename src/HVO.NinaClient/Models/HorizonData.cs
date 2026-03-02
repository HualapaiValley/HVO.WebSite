using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Horizon data for the active profile from NINA Advanced API
/// </summary>
public class HorizonData
{
    [JsonPropertyName("Altitudes")]
    public IReadOnlyList<double> Altitudes { get; set; } = [];

    [JsonPropertyName("Azimuths")]
    public IReadOnlyList<double> Azimuths { get; set; } = [];
}

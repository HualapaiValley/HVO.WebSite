using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Planetarium settings from NINA profile
/// </summary>
public record PlanetariumSettings
{
    [JsonPropertyName("CdCPath")]
    public string CdCPath { get; init; } = "";

    [JsonPropertyName("HNSKYPath")]
    public string HNSKYPath { get; init; } = "";

    [JsonPropertyName("PlanetariumName")]
    public string PlanetariumName { get; init; } = "";

    [JsonPropertyName("PreferredPlanetarium")]
    public string PreferredPlanetarium { get; init; } = "";

    [JsonPropertyName("StellariumHost")]
    public string StellariumHost { get; init; } = "";

    [JsonPropertyName("StellariumPort")]
    public int StellariumPort { get; init; }

    [JsonPropertyName("TheSkyXHost")]
    public string TheSkyXHost { get; init; } = "";

    [JsonPropertyName("TheSkyXPort")]
    public int TheSkyXPort { get; init; }
}

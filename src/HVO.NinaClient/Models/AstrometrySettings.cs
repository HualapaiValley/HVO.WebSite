using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Astrometry settings for NINA profile
/// </summary>
public class AstrometrySettings
{
    [JsonPropertyName("Latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("Longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("Elevation")]
    public int Elevation { get; set; }

    [JsonPropertyName("HorizonFilePath")]
    public string HorizonFilePath { get; set; } = string.Empty;
}

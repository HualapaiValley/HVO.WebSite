using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// SnapShotControlSettings for NINA profile - simplified version
/// </summary>
public class SnapShotControlSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}

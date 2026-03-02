using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Equipment device information base class
/// </summary>
public record DeviceInfo
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("Description")]
    public string? Description { get; init; }

    [JsonPropertyName("DeviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("DriverInfo")]
    public string? DriverInfo { get; init; }

    [JsonPropertyName("DriverVersion")]
    public string? DriverVersion { get; init; }

    [JsonPropertyName("Connected")]
    public bool Connected { get; init; }
}

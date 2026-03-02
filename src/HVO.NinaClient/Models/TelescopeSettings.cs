using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Telescope settings from NINA profile
/// </summary>
public record TelescopeSettings
{
    [JsonPropertyName("EnableAtPark")]
    public bool EnableAtPark { get; init; }

    [JsonPropertyName("FocalLength")]
    public double FocalLength { get; init; }

    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("NoSync")]
    public bool NoSync { get; init; }

    [JsonPropertyName("PrimaryReversed")]
    public bool PrimaryReversed { get; init; }

    [JsonPropertyName("SecondaryReversed")]
    public bool SecondaryReversed { get; init; }

    [JsonPropertyName("SettleTime")]
    public double SettleTime { get; init; }

    [JsonPropertyName("SnapPortStop")]
    public bool SnapPortStop { get; init; }

    [JsonPropertyName("TrackingMode")]
    public TelescopeTrackingMode TrackingMode { get; init; }
}

using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Filter wheel settings for NINA profile
/// </summary>
public class FilterWheelSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("FilterWheelFilters")]
    public FilterWheelFilter[] FilterWheelFilters { get; set; } = Array.Empty<FilterWheelFilter>();

    [JsonPropertyName("DisableGuidingOnFilterChange")]
    public bool DisableGuidingOnFilterChange { get; set; }

    [JsonPropertyName("Unidirectional")]
    public bool Unidirectional { get; set; }
}

using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Filter wheel information
/// </summary>
public record FilterWheelInfo : DeviceInfo
{
    [JsonPropertyName("IsMoving")]
    public bool IsMoving { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<string>? SupportedActions { get; init; }

    [JsonPropertyName("SelectedFilter")]
    public FilterInfo? SelectedFilter { get; init; }

    [JsonPropertyName("AvailableFilters")]
    public List<FilterInfo>? AvailableFilters { get; init; }
}

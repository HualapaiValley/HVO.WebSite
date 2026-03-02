using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Image file settings from NINA profile
/// </summary>
public record ImageFileSettings
{
    [JsonPropertyName("FilePattern")]
    public string FilePattern { get; init; } = "";

    [JsonPropertyName("FileType")]
    public string FileType { get; init; } = "";

    [JsonPropertyName("FilePath")]
    public string FilePath { get; init; } = "";
}

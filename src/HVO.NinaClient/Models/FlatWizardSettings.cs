using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// FlatWizardSettings for NINA profile - simplified version
/// </summary>
public class FlatWizardSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;
}

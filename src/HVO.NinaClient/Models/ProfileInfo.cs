using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Profile information from NINA Advanced API v2.2.7
/// Complete model matching the official API specification
/// </summary>
public class ProfileInfo
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("LastUsed")]
    public string LastUsed { get; set; } = string.Empty;

    [JsonPropertyName("ApplicationSettings")]
    public ApplicationSettings? ApplicationSettings { get; set; }

    [JsonPropertyName("AstrometrySettings")]
    public AstrometrySettings? AstrometrySettings { get; set; }

    [JsonPropertyName("CameraSettings")]
    public CameraSettings? CameraSettings { get; set; }

    [JsonPropertyName("ColorSchemaSettings")]
    public ColorSchemaSettings? ColorSchemaSettings { get; set; }

    [JsonPropertyName("DomeSettings")]
    public DomeSettings? DomeSettings { get; set; }

    [JsonPropertyName("FilterWheelSettings")]
    public FilterWheelSettings? FilterWheelSettings { get; set; }

    [JsonPropertyName("FlatWizardSettings")]
    public FlatWizardSettings? FlatWizardSettings { get; set; }

    [JsonPropertyName("FocuserSettings")]
    public FocuserSettings? FocuserSettings { get; set; }

    [JsonPropertyName("FramingAssistantSettings")]
    public FramingAssistantSettings? FramingAssistantSettings { get; set; }

    [JsonPropertyName("GuiderSettings")]
    public GuiderSettings? GuiderSettings { get; set; }

    [JsonPropertyName("ImageFileSettings")]
    public ImageFileSettings? ImageFileSettings { get; set; }

    [JsonPropertyName("ImageSettings")]
    public ImageSettings? ImageSettings { get; set; }

    [JsonPropertyName("MeridianFlipSettings")]
    public MeridianFlipSettings? MeridianFlipSettings { get; set; }

    [JsonPropertyName("PlanetariumSettings")]
    public PlanetariumSettings? PlanetariumSettings { get; set; }

    [JsonPropertyName("PlateSolveSettings")]
    public PlateSolveSettings? PlateSolveSettings { get; set; }

    [JsonPropertyName("RotatorSettings")]
    public RotatorSettings? RotatorSettings { get; set; }

    [JsonPropertyName("FlatDeviceSettings")]
    public FlatDeviceSettings? FlatDeviceSettings { get; set; }

    [JsonPropertyName("SequenceSettings")]
    public SequenceSettings? SequenceSettings { get; set; }

    [JsonPropertyName("SwitchSettings")]
    public SwitchSettings? SwitchSettings { get; set; }

    [JsonPropertyName("TelescopeSettings")]
    public TelescopeSettings? TelescopeSettings { get; set; }

    [JsonPropertyName("WeatherDataSettings")]
    public WeatherDataSettings? WeatherDataSettings { get; set; }

    [JsonPropertyName("SnapShotControlSettings")]
    public SnapShotControlSettings? SnapShotControlSettings { get; set; }

    [JsonPropertyName("SafetyMonitorSettings")]
    public SafetyMonitorSettings? SafetyMonitorSettings { get; set; }

    [JsonPropertyName("AlpacaSettings")]
    public AlpacaSettings? AlpacaSettings { get; set; }

    [JsonPropertyName("ImageHistorySettings")]
    public ImageHistorySettings? ImageHistorySettings { get; set; }
}

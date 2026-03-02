using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Focuser settings for NINA profile
/// </summary>
public class FocuserSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("AutoFocusExposureTime")]
    public int AutoFocusExposureTime { get; set; }

    [JsonPropertyName("AutoFocusInitialOffsetSteps")]
    public int AutoFocusInitialOffsetSteps { get; set; }

    [JsonPropertyName("AutoFocusStepSize")]
    public int AutoFocusStepSize { get; set; }

    [JsonPropertyName("UseFilterWheelOffsets")]
    public bool UseFilterWheelOffsets { get; set; }

    [JsonPropertyName("AutoFocusDisableGuiding")]
    public bool AutoFocusDisableGuiding { get; set; }

    [JsonPropertyName("FocuserSettleTime")]
    public int FocuserSettleTime { get; set; }

    [JsonPropertyName("AutoFocusTotalNumberOfAttempts")]
    public int AutoFocusTotalNumberOfAttempts { get; set; }

    [JsonPropertyName("AutoFocusNumberOfFramesPerPoint")]
    public int AutoFocusNumberOfFramesPerPoint { get; set; }

    [JsonPropertyName("AutoFocusInnerCropRatio")]
    public int AutoFocusInnerCropRatio { get; set; }

    [JsonPropertyName("AutoFocusOuterCropRatio")]
    public int AutoFocusOuterCropRatio { get; set; }

    [JsonPropertyName("AutoFocusUseBrightestStars")]
    public int AutoFocusUseBrightestStars { get; set; }

    [JsonPropertyName("BacklashIn")]
    public int BacklashIn { get; set; }

    [JsonPropertyName("BacklashOut")]
    public int BacklashOut { get; set; }

    [JsonPropertyName("AutoFocusBinning")]
    public int AutoFocusBinning { get; set; }

    [JsonPropertyName("AutoFocusCurveFitting")]
    public AutoFocusCurveFitting AutoFocusCurveFitting { get; set; }

    [JsonPropertyName("AutoFocusMethod")]
    public AutoFocusMethod AutoFocusMethod { get; set; }

    [JsonPropertyName("ContrastDetectionMethod")]
    public ContrastDetectionMethod ContrastDetectionMethod { get; set; }

    [JsonPropertyName("BacklashCompensationModel")]
    public BacklashCompensationModel BacklashCompensationModel { get; set; }

    [JsonPropertyName("AutoFocusTimeoutSeconds")]
    public int AutoFocusTimeoutSeconds { get; set; }

    [JsonPropertyName("RSquaredThreshold")]
    public double RSquaredThreshold { get; set; }
}

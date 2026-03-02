using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Camera settings for NINA profile
/// </summary>
public class CameraSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("PixelSize")]
    public int PixelSize { get; set; }

    [JsonPropertyName("BitDepth")]
    public int BitDepth { get; set; }

    [JsonPropertyName("BulbMode")]
    public BulbMode BulbMode { get; set; }

    [JsonPropertyName("RawConverter")]
    public RawConverter RawConverter { get; set; }

    [JsonPropertyName("SerialPort")]
    public string SerialPort { get; set; } = string.Empty;

    [JsonPropertyName("MinFlatExposureTime")]
    public double MinFlatExposureTime { get; set; }

    [JsonPropertyName("MaxFlatExposureTime")]
    public int MaxFlatExposureTime { get; set; }

    [JsonPropertyName("FileCameraFolder")]
    public string FileCameraFolder { get; set; } = string.Empty;

    [JsonPropertyName("FileCameraUseBulbMode")]
    public bool FileCameraUseBulbMode { get; set; }

    [JsonPropertyName("FileCameraIsBayered")]
    public bool FileCameraIsBayered { get; set; }

    [JsonPropertyName("FileCameraExtension")]
    public string FileCameraExtension { get; set; } = string.Empty;

    [JsonPropertyName("FileCameraAlwaysListen")]
    public bool FileCameraAlwaysListen { get; set; }

    [JsonPropertyName("FileCameraDownloadDelay")]
    public int FileCameraDownloadDelay { get; set; }

    [JsonPropertyName("BayerPattern")]
    public BayerPattern BayerPattern { get; set; }

    [JsonPropertyName("FLIEnableFloodFlush")]
    public bool FLIEnableFloodFlush { get; set; }

    [JsonPropertyName("FLIEnableSnapshotFloodFlush")]
    public bool FLIEnableSnapshotFloodFlush { get; set; }

    [JsonPropertyName("FLIFloodDuration")]
    public int FLIFloodDuration { get; set; }

    [JsonPropertyName("FLIFlushCount")]
    public int FLIFlushCount { get; set; }

    [JsonPropertyName("BitScaling")]
    public bool BitScaling { get; set; }

    [JsonPropertyName("CoolingDuration")]
    public int CoolingDuration { get; set; }

    [JsonPropertyName("WarmingDuration")]
    public int WarmingDuration { get; set; }

    [JsonPropertyName("Temperature")]
    public int Temperature { get; set; }

    [JsonPropertyName("Gain")]
    public int Gain { get; set; }

    [JsonPropertyName("Offset")]
    public int Offset { get; set; }

    [JsonPropertyName("QhyIncludeOverscan")]
    public bool QhyIncludeOverscan { get; set; }

    [JsonPropertyName("Timeout")]
    public int Timeout { get; set; }

    [JsonPropertyName("DewHeaterOn")]
    public bool DewHeaterOn { get; set; }

    [JsonPropertyName("ASCOMAllowUnevenPixelDimension")]
    public bool ASCOMAllowUnevenPixelDimension { get; set; }

    [JsonPropertyName("MirrorLockupDelay")]
    public int MirrorLockupDelay { get; set; }

    [JsonPropertyName("BinAverageEnabled")]
    public bool BinAverageEnabled { get; set; }

    [JsonPropertyName("TrackingCameraASCOMServerEnabled")]
    public bool TrackingCameraASCOMServerEnabled { get; set; }

    [JsonPropertyName("TrackingCameraASCOMServerPipeName")]
    public string TrackingCameraASCOMServerPipeName { get; set; } = string.Empty;

    [JsonPropertyName("TrackingCameraASCOMServerLoggingEnabled")]
    public bool TrackingCameraASCOMServerLoggingEnabled { get; set; }

    [JsonPropertyName("SBIGUseExternalCcdTracker")]
    public bool SBIGUseExternalCcdTracker { get; set; }

    [JsonPropertyName("AtikGainPreset")]
    public int AtikGainPreset { get; set; }

    [JsonPropertyName("AtikExposureSpeed")]
    public int AtikExposureSpeed { get; set; }

    [JsonPropertyName("AtikWindowHeaterPowerLevel")]
    public int AtikWindowHeaterPowerLevel { get; set; }

    [JsonPropertyName("TouptekAlikeUltraMode")]
    public bool TouptekAlikeUltraMode { get; set; }

    [JsonPropertyName("TouptekAlikeHighFullwell")]
    public bool TouptekAlikeHighFullwell { get; set; }

    [JsonPropertyName("TouptekAlikeLEDLights")]
    public bool TouptekAlikeLEDLights { get; set; }

    [JsonPropertyName("TouptekAlikeDewHeaterStrength")]
    public int TouptekAlikeDewHeaterStrength { get; set; }

    [JsonPropertyName("GenericCameraDewHeaterStrength")]
    public int GenericCameraDewHeaterStrength { get; set; }

    [JsonPropertyName("GenericCameraFanSpeed")]
    public int GenericCameraFanSpeed { get; set; }

    [JsonPropertyName("ZwoAsiMonoBinMode")]
    public bool ZwoAsiMonoBinMode { get; set; }

    [JsonPropertyName("ASCOMCreate32BitData")]
    public bool ASCOMCreate32BitData { get; set; }

    [JsonPropertyName("BadPixelCorrection")]
    public bool BadPixelCorrection { get; set; }

    [JsonPropertyName("BadPixelCorrectionThreshold")]
    public int BadPixelCorrectionThreshold { get; set; }
}

using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Camera information from NINA Advanced API
/// Validates against OpenAPI specification v2.2.6
/// </summary>
public record CameraInfo : DeviceInfo
{
    // Temperature Control Properties
    [JsonPropertyName("TargetTemp")]
    public double TargetTemp { get; init; }

    [JsonPropertyName("AtTargetTemp")]
    public bool AtTargetTemp { get; init; }

    [JsonPropertyName("CanSetTemperature")]
    public bool CanSetTemperature { get; init; }

    [JsonPropertyName("Temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("CoolerOn")]
    public bool CoolerOn { get; init; }

    [JsonPropertyName("CoolerPower")]
    public double CoolerPower { get; init; }

    [JsonPropertyName("TemperatureSetPoint")]
    public double TemperatureSetPoint { get; init; }

    // Camera Capabilities
    [JsonPropertyName("HasShutter")]
    public bool HasShutter { get; init; }

    [JsonPropertyName("CanSubSample")]
    public bool CanSubSample { get; init; }

    [JsonPropertyName("CanSetOffset")]
    public bool CanSetOffset { get; init; }

    [JsonPropertyName("CanGetGain")]
    public bool CanGetGain { get; init; }

    [JsonPropertyName("CanSetGain")]
    public bool CanSetGain { get; init; }

    [JsonPropertyName("HasDewHeater")]
    public bool HasDewHeater { get; init; }

    [JsonPropertyName("DewHeaterOn")]
    public bool DewHeaterOn { get; init; }

    [JsonPropertyName("CanSetUSBLimit")]
    public bool CanSetUSBLimit { get; init; }

    [JsonPropertyName("LiveViewEnabled")]
    public bool LiveViewEnabled { get; init; }

    [JsonPropertyName("CanShowLiveView")]
    public bool CanShowLiveView { get; init; }

    // Gain and Offset Properties  
    [JsonPropertyName("Gain")]
    public int Gain { get; init; }

    [JsonPropertyName("DefaultGain")]
    public int DefaultGain { get; init; }

    [JsonPropertyName("GainMin")]
    public int GainMin { get; init; }

    [JsonPropertyName("GainMax")]
    public int GainMax { get; init; }

    [JsonPropertyName("Gains")]
    public List<string>? Gains { get; init; }

    [JsonPropertyName("Offset")]
    public int Offset { get; init; }

    [JsonPropertyName("DefaultOffset")]
    public int DefaultOffset { get; init; }

    [JsonPropertyName("OffsetMin")]
    public int OffsetMin { get; init; }

    [JsonPropertyName("OffsetMax")]
    public int OffsetMax { get; init; }

    // Camera Dimensions and Properties
    [JsonPropertyName("XSize")]
    public int XSize { get; init; }

    [JsonPropertyName("YSize")]
    public int YSize { get; init; }

    [JsonPropertyName("PixelSize")]
    public double PixelSize { get; init; }

    [JsonPropertyName("BitDepth")]
    public int BitDepth { get; init; }

    // Binning Properties
    [JsonPropertyName("BinX")]
    public int BinX { get; init; }

    [JsonPropertyName("BinY")]
    public int BinY { get; init; }

    [JsonPropertyName("BinningModes")]
    public List<BinningMode>? BinningModes { get; init; }

    // Bayer Pattern Properties
    [JsonPropertyName("SensorType")]
    public string? SensorType { get; init; }

    [JsonPropertyName("BayerOffsetX")]
    public int BayerOffsetX { get; init; }

    [JsonPropertyName("BayerOffsetY")]
    public int BayerOffsetY { get; init; }

    // Camera State Properties
    [JsonPropertyName("CameraState")]
    public string? CameraState { get; init; }

    [JsonPropertyName("IsExposing")]
    public bool IsExposing { get; init; }

    [JsonPropertyName("ExposureEndTime")]
    public string? ExposureEndTime { get; init; }

    [JsonPropertyName("LastDownloadTime")]
    public double LastDownloadTime { get; init; }

    // SubFrame Properties
    [JsonPropertyName("IsSubSampleEnabled")]
    public bool IsSubSampleEnabled { get; init; }

    [JsonPropertyName("SubSampleX")]
    public int SubSampleX { get; init; }

    [JsonPropertyName("SubSampleY")]
    public int SubSampleY { get; init; }

    [JsonPropertyName("SubSampleWidth")]
    public int SubSampleWidth { get; init; }

    [JsonPropertyName("SubSampleHeight")]
    public int SubSampleHeight { get; init; }

    // Readout Mode Properties
    [JsonPropertyName("ReadoutModes")]
    public List<string>? ReadoutModes { get; init; }

    [JsonPropertyName("ReadoutMode")]
    public int ReadoutMode { get; init; }

    [JsonPropertyName("ReadoutModeForSnapImages")]
    public int ReadoutModeForSnapImages { get; init; }

    [JsonPropertyName("ReadoutModeForNormalImages")]
    public int ReadoutModeForNormalImages { get; init; }

    // Exposure Properties
    [JsonPropertyName("ExposureMax")]
    public double ExposureMax { get; init; }

    [JsonPropertyName("ExposureMin")]
    public double ExposureMin { get; init; }

    // USB Limit Properties
    [JsonPropertyName("USBLimit")]
    public int USBLimit { get; init; }

    [JsonPropertyName("USBLimitMin")]
    public int USBLimitMin { get; init; }

    [JsonPropertyName("USBLimitMax")]
    public int USBLimitMax { get; init; }

    // Electronics Properties
    [JsonPropertyName("ElectronsPerADU")]
    public double ElectronsPerADU { get; init; }

    [JsonPropertyName("Battery")]
    public int Battery { get; init; }

    // Actions
    [JsonPropertyName("SupportedActions")]
    public List<string>? SupportedActions { get; init; }

    // Removed properties that don't exist in OpenAPI specification:
    // - CanSetCCDTemperature (use CanSetTemperature)
    // - CanSetCoolerPower (not in spec)
    // - CanSubFrame (use CanSubSample)
    // - CanGetCoolerPower (not in spec)
    // - CCDTemperature (use Temperature)
    // - HeatSinkTemperature (not in spec)
    // - ImageReady (not in spec)
    // - IsPulseGuiding (not in spec)
    // - MaxADU (not in spec)
    // - MaxBinX (not in spec)
    // - MaxBinY (not in spec)
    // - NumX (not in spec)
    // - NumY (not in spec)
    // - PixelSizeX (use PixelSize)
    // - PixelSizeY (use PixelSize)
    // - SetCCDTemperature (use TemperatureSetPoint)
    // - StartX (not in spec)
    // - StartY (not in spec)
    // - SubExposureDuration (not in spec)
    // - CanAbortExposure (not in spec)
    // - CanAsymmetricBin (not in spec)
    // - CanFastReadout (not in spec)
    // - CanStopExposure (not in spec)
    // - CameraXSize (use XSize)
    // - CameraYSize (use YSize)
    // - ExposureResolution (not in spec)
    // - FastReadout (not in spec)
    // - FullWellCapacity (not in spec)
    // - Offsets (not in spec)
    // - PercentCompleted (not in spec)
    // - SensorName (not in spec)
}

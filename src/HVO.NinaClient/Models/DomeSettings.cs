using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Dome settings for NINA profile
/// </summary>
public class DomeSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("ScopePositionEastWest_mm")]
    public int ScopePositionEastWest_mm { get; set; }

    [JsonPropertyName("ScopePositionNorthSouth_mm")]
    public int ScopePositionNorthSouth_mm { get; set; }

    [JsonPropertyName("ScopePositionUpDown_mm")]
    public int ScopePositionUpDown_mm { get; set; }

    [JsonPropertyName("DomeRadius_mm")]
    public int DomeRadius_mm { get; set; }

    [JsonPropertyName("GemAxis_mm")]
    public int GemAxis_mm { get; set; }

    [JsonPropertyName("LateralAxis_mm")]
    public int LateralAxis_mm { get; set; }

    [JsonPropertyName("AzimuthTolerance_degrees")]
    public int AzimuthTolerance_degrees { get; set; }

    [JsonPropertyName("FindHomeBeforePark")]
    public bool FindHomeBeforePark { get; set; }

    [JsonPropertyName("DomeSyncTimeoutSeconds")]
    public int DomeSyncTimeoutSeconds { get; set; }

    [JsonPropertyName("SynchronizeDuringMountSlew")]
    public bool SynchronizeDuringMountSlew { get; set; }

    [JsonPropertyName("SyncSlewDomeWhenMountSlews")]
    public bool SyncSlewDomeWhenMountSlews { get; set; }

    [JsonPropertyName("RotateDegrees")]
    public int RotateDegrees { get; set; }

    [JsonPropertyName("CloseOnUnsafe")]
    public bool CloseOnUnsafe { get; set; }

    [JsonPropertyName("ParkMountBeforeShutterMove")]
    public bool ParkMountBeforeShutterMove { get; set; }

    [JsonPropertyName("RefuseUnsafeShutterMove")]
    public bool RefuseUnsafeShutterMove { get; set; }

    [JsonPropertyName("RefuseUnsafeShutterOpenSansSafetyDevice")]
    public bool RefuseUnsafeShutterOpenSansSafetyDevice { get; set; }

    [JsonPropertyName("ParkDomeBeforeShutterMove")]
    public bool ParkDomeBeforeShutterMove { get; set; }

    [JsonPropertyName("MountType")]
    public MountType MountType { get; set; }

    [JsonPropertyName("DecOffsetHorizontal_mm")]
    public int DecOffsetHorizontal_mm { get; set; }

    [JsonPropertyName("SettleTimeSeconds")]
    public int SettleTimeSeconds { get; set; }
}

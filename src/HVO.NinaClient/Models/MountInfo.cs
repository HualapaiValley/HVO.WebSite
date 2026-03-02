using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Mount information
/// </summary>
public record MountInfo : DeviceInfo
{
    [JsonPropertyName("SiderealTime")]
    public double SiderealTime { get; init; }

    [JsonPropertyName("RightAscension")]
    public double RightAscension { get; init; }

    [JsonPropertyName("Declination")]
    public double Declination { get; init; }

    [JsonPropertyName("SiteLatitude")]
    public double SiteLatitude { get; init; }

    [JsonPropertyName("SiteLongitude")]
    public double SiteLongitude { get; init; }

    [JsonPropertyName("SiteElevation")]
    public double SiteElevation { get; init; }

    [JsonPropertyName("RightAscensionString")]
    public string? RightAscensionString { get; init; }

    [JsonPropertyName("DeclinationString")]
    public string? DeclinationString { get; init; }

    [JsonPropertyName("Coordinates")]
    public CoordinateInfo? Coordinates { get; init; }

    [JsonPropertyName("TimeToMeridianFlip")]
    public double TimeToMeridianFlip { get; init; }

    [JsonPropertyName("SideOfPier")]
    public string? SideOfPier { get; init; }

    [JsonPropertyName("Altitude")]
    public double Altitude { get; init; }

    [JsonPropertyName("AltitudeString")]
    public string? AltitudeString { get; init; }

    [JsonPropertyName("Azimuth")]
    public double Azimuth { get; init; }

    [JsonPropertyName("AzimuthString")]
    public string? AzimuthString { get; init; }

    [JsonPropertyName("SiderealTimeString")]
    public string? SiderealTimeString { get; init; }

    [JsonPropertyName("HoursToMeridianString")]
    public string? HoursToMeridianString { get; init; }

    [JsonPropertyName("AtPark")]
    public bool AtPark { get; init; }

    [JsonPropertyName("TrackingRate")]
    public object? TrackingRate { get; init; }

    [JsonPropertyName("TrackingEnabled")]
    public bool TrackingEnabled { get; init; }

    [JsonPropertyName("TrackingModes")]
    public List<string>? TrackingModes { get; init; }

    [JsonPropertyName("AtHome")]
    public bool AtHome { get; init; }

    [JsonPropertyName("CanFindHome")]
    public bool CanFindHome { get; init; }

    [JsonPropertyName("CanPark")]
    public bool CanPark { get; init; }

    [JsonPropertyName("CanSetPark")]
    public bool CanSetPark { get; init; }

    [JsonPropertyName("CanSetTrackingEnabled")]
    public bool CanSetTrackingEnabled { get; init; }

    [JsonPropertyName("CanSetDeclinationRate")]
    public bool CanSetDeclinationRate { get; init; }

    [JsonPropertyName("CanSetRightAscensionRate")]
    public bool CanSetRightAscensionRate { get; init; }

    [JsonPropertyName("EquatorialSystem")]
    public string? EquatorialSystem { get; init; }

    [JsonPropertyName("HasUnknownEpoch")]
    public bool HasUnknownEpoch { get; init; }

    [JsonPropertyName("TimeToMeridianFlipString")]
    public string? TimeToMeridianFlipString { get; init; }

    [JsonPropertyName("Slewing")]
    public bool Slewing { get; init; }

    [JsonPropertyName("GuideRateRightAscensionArcsecPerSec")]
    public double GuideRateRightAscensionArcsecPerSec { get; init; }

    [JsonPropertyName("GuideRateDeclinationArcsecPerSec")]
    public double GuideRateDeclinationArcsecPerSec { get; init; }

    [JsonPropertyName("CanMovePrimaryAxis")]
    public bool CanMovePrimaryAxis { get; init; }

    [JsonPropertyName("CanMoveSecondaryAxis")]
    public bool CanMoveSecondaryAxis { get; init; }

    [JsonPropertyName("PrimaryAxisRates")]
    public List<object>? PrimaryAxisRates { get; init; }

    [JsonPropertyName("SecondaryAxisRates")]
    public List<object>? SecondaryAxisRates { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<string>? SupportedActions { get; init; }

    [JsonPropertyName("AlignmentMode")]
    public string? AlignmentMode { get; init; }

    [JsonPropertyName("CanPulseGuide")]
    public bool CanPulseGuide { get; init; }

    [JsonPropertyName("IsPulseGuiding")]
    public bool IsPulseGuiding { get; init; }

    [JsonPropertyName("CanSetPierSide")]
    public bool CanSetPierSide { get; init; }

    [JsonPropertyName("CanSlew")]
    public bool CanSlew { get; init; }

    [JsonPropertyName("UTCDate")]
    public string? UTCDate { get; init; }
}

using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Dome information
/// </summary>
public record DomeInfo : DeviceInfo
{
    [JsonPropertyName("ShutterStatus")]
    public string? ShutterStatus { get; init; }

    [JsonPropertyName("DriverCanFollow")]
    public bool DriverCanFollow { get; init; }

    [JsonPropertyName("CanSetShutter")]
    public bool CanSetShutter { get; init; }

    [JsonPropertyName("CanSetPark")]
    public bool CanSetPark { get; init; }

    [JsonPropertyName("CanSetAzimuth")]
    public bool CanSetAzimuth { get; init; }

    [JsonPropertyName("CanSyncAzimuth")]
    public bool CanSyncAzimuth { get; init; }

    [JsonPropertyName("CanPark")]
    public bool CanPark { get; init; }

    [JsonPropertyName("CanFindHome")]
    public bool CanFindHome { get; init; }

    [JsonPropertyName("AtPark")]
    public bool AtPark { get; init; }

    [JsonPropertyName("AtHome")]
    public bool AtHome { get; init; }

    [JsonPropertyName("DriverFollowing")]
    public bool DriverFollowing { get; init; }

    [JsonPropertyName("Slewing")]
    public bool Slewing { get; init; }

    [JsonPropertyName("Azimuth")]
    public int Azimuth { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<object>? SupportedActions { get; init; }

    [JsonPropertyName("IsFollowing")]
    public bool IsFollowing { get; init; }

    [JsonPropertyName("IsSynchronized")]
    public bool IsSynchronized { get; init; }
}

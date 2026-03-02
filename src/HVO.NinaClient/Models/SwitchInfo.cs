using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Switch information
/// </summary>
public record SwitchInfo : DeviceInfo
{
    [JsonPropertyName("WritableSwitches")]
    public List<WritableSwitch>? WritableSwitches { get; init; }

    [JsonPropertyName("ReadonlySwitches")]
    public List<ReadonlySwitch>? ReadonlySwitches { get; init; }

    [JsonPropertyName("SupportedActions")]
    public List<string>? SupportedActions { get; init; }
}

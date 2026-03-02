using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Flat device info response
/// </summary>
public record FlatDeviceInfoResponse : NinaApiResponse<FlatDeviceInfo>;

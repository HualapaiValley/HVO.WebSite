using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Device list response
/// </summary>
public record DeviceListResponse : NinaApiResponse<List<DeviceInfo>>;

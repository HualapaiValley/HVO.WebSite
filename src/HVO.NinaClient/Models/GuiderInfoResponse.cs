using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Guider info response
/// </summary>
public record GuiderInfoResponse : NinaApiResponse<GuiderInfo>;

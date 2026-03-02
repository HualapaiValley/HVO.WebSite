using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Camera info response
/// </summary>
public record CameraInfoResponse : NinaApiResponse<CameraInfo>;

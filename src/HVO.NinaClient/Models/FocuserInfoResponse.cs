using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Focuser info response
/// </summary>
public record FocuserInfoResponse : NinaApiResponse<FocuserInfo>;

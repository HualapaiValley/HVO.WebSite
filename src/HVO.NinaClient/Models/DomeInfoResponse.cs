using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Dome info response
/// </summary>
public record DomeInfoResponse : NinaApiResponse<DomeInfo>;

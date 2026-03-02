using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Guide step list response
/// </summary>
public record GuideStepListResponse : NinaApiResponse<List<GuideStep>>;

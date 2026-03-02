using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Image statistics response
/// </summary>
public record ImageStatisticsResponse : NinaApiResponse<ImageStatistics>;

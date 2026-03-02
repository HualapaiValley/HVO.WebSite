using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Weather info response
/// </summary>
public record WeatherInfoResponse : NinaApiResponse<WeatherInfo>;

using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Safety monitor info response
/// </summary>
public record SafetyMonitorInfoResponse : NinaApiResponse<SafetyMonitorInfo>;

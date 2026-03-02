using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Focuser last autofocus response
/// </summary>
public record FocuserLastAFResponse : NinaApiResponse<FocuserLastAF>;

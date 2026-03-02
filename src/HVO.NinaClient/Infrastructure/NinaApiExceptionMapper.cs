using System.Net;
using HVO.NinaClient.Exceptions;

namespace HVO.NinaClient.Infrastructure;

/// <summary>
/// Maps HTTP status codes and NINA API errors to appropriate exception types
/// </summary>
internal static class NinaApiExceptionMapper
{
    /// <summary>
    /// Maps HTTP status code to appropriate NINA API exception
    /// </summary>
    /// <param name="statusCode">HTTP status code</param>
    /// <param name="responseContent">Response content for additional context</param>
    /// <param name="endpoint">API endpoint that was called</param>
    /// <returns>Appropriate NinaApiException instance</returns>
    public static NinaApiException MapHttpStatusToException(
        HttpStatusCode statusCode, 
        string? responseContent = null, 
        string? endpoint = null)
    {
        return statusCode switch
        {
            HttpStatusCode.NotFound => new NinaEquipmentNotFoundException("Equipment or resource", endpoint),
            HttpStatusCode.BadRequest => DetermineBadRequestException(responseContent, endpoint),
            HttpStatusCode.Unauthorized => new NinaApiHttpException(statusCode, responseContent, endpoint),
            HttpStatusCode.Forbidden => new NinaApiHttpException(statusCode, responseContent, endpoint),
            HttpStatusCode.InternalServerError => new NinaApiHttpException(statusCode, responseContent, endpoint),
            HttpStatusCode.ServiceUnavailable => new NinaConnectionException(
                $"NINA API service unavailable{(endpoint != null ? $" at {endpoint}" : "")}", endpoint),
            HttpStatusCode.RequestTimeout => new NinaConnectionException(
                $"NINA API request timeout{(endpoint != null ? $" at {endpoint}" : "")}", endpoint),
            _ => new NinaApiHttpException(statusCode, responseContent, endpoint)
        };
    }

    /// <summary>
    /// Maps NINA API logical errors to appropriate exception types
    /// </summary>
    /// <param name="apiError">Error message from NINA API</param>
    /// <param name="endpoint">API endpoint that was called</param>
    /// <returns>Appropriate NinaApiException instance</returns>
    public static NinaApiException MapApiErrorToException(string apiError, string? endpoint = null)
    {
        // Analyze error message for specific equipment or operation issues
        var lowerError = apiError.ToLowerInvariant();

        if (lowerError.Contains("not connected") || lowerError.Contains("not found"))
        {
            var equipmentType = ExtractEquipmentType(lowerError);
            return new NinaEquipmentNotFoundException(equipmentType ?? "Unknown equipment", endpoint);
        }

        if (lowerError.Contains("not available") || lowerError.Contains("not supported") || lowerError.Contains("cannot"))
        {
            var (equipmentType, operation) = ExtractEquipmentAndOperation(lowerError, endpoint);
            return new NinaEquipmentNotAvailableException(equipmentType ?? "Unknown equipment", operation ?? "Unknown operation", endpoint);
        }

        // Default to logical exception
        return new NinaApiLogicalException(apiError, endpoint);
    }

    private static NinaApiException DetermineBadRequestException(string? responseContent, string? endpoint)
    {
        if (string.IsNullOrEmpty(responseContent))
        {
            return new NinaApiHttpException(HttpStatusCode.BadRequest, responseContent, endpoint);
        }

        var lowerContent = responseContent.ToLowerInvariant();
        if (lowerContent.Contains("not connected") || lowerContent.Contains("not found"))
        {
            var equipmentType = ExtractEquipmentType(lowerContent);
            return new NinaEquipmentNotFoundException(equipmentType ?? "Equipment", endpoint);
        }

        if (lowerContent.Contains("not available") || lowerContent.Contains("not supported"))
        {
            var (equipmentType, operation) = ExtractEquipmentAndOperation(lowerContent, endpoint);
            return new NinaEquipmentNotAvailableException(equipmentType ?? "Equipment", operation ?? "Operation", endpoint);
        }

        return new NinaApiHttpException(HttpStatusCode.BadRequest, responseContent, endpoint);
    }

    private static string? ExtractEquipmentType(string errorMessage)
    {
        // Try to extract equipment type from common NINA error patterns
        var equipmentKeywords = new[] { "camera", "mount", "focuser", "rotator", "filterwheel", "guider", "dome", "switch", "weather" };
        
        foreach (var keyword in equipmentKeywords)
        {
            if (errorMessage.Contains(keyword))
            {
                return char.ToUpperInvariant(keyword[0]) + keyword[1..];
            }
        }

        return null;
    }

    private static (string? equipmentType, string? operation) ExtractEquipmentAndOperation(string errorMessage, string? endpoint)
    {
        var equipmentType = ExtractEquipmentType(errorMessage);
        
        // Try to extract operation from endpoint if available
        string? operation = null;
        if (!string.IsNullOrEmpty(endpoint))
        {
            var segments = endpoint.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                operation = segments[^1]; // Last segment often contains the operation
            }
        }

        // Try to extract operation from error message
        var operationKeywords = new[] { "connect", "disconnect", "start", "stop", "move", "capture", "abort", "park", "home" };
        foreach (var keyword in operationKeywords)
        {
            if (errorMessage.Contains(keyword))
            {
                operation = keyword;
                break;
            }
        }

        return (equipmentType, operation);
    }
}

using System.Net;

namespace HVO.NinaClient.Exceptions;

/// <summary>
/// Base exception for NINA API operations
/// </summary>
public class NinaApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? ApiError { get; }
    public string? Endpoint { get; }

    public NinaApiException(string message) : base(message)
    {
    }

    public NinaApiException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public NinaApiException(
        string message, 
        HttpStatusCode statusCode, 
        string? apiError = null, 
        string? endpoint = null) : base(message)
    {
        StatusCode = statusCode;
        ApiError = apiError;
        Endpoint = endpoint;
    }

    public NinaApiException(
        string message, 
        HttpStatusCode statusCode, 
        Exception innerException, 
        string? apiError = null, 
        string? endpoint = null) : base(message, innerException)
    {
        StatusCode = statusCode;
        ApiError = apiError;
        Endpoint = endpoint;
    }
}

/// <summary>
/// Exception thrown when NINA API returns a logical error (Success = false)
/// </summary>
public class NinaApiLogicalException : NinaApiException
{
    public NinaApiLogicalException(string apiError, string? endpoint = null) 
        : base($"NINA API logical error: {apiError}", HttpStatusCode.OK, apiError, endpoint)
    {
    }
}

/// <summary>
/// Exception thrown when NINA API returns an HTTP error status
/// </summary>
public class NinaApiHttpException : NinaApiException
{
    public string? ResponseContent { get; }

    public NinaApiHttpException(
        HttpStatusCode statusCode, 
        string? responseContent = null, 
        string? endpoint = null) 
        : base($"NINA API HTTP error {(int)statusCode} ({statusCode})" + 
               (string.IsNullOrEmpty(responseContent) ? "" : $": {responseContent}"), 
               statusCode, responseContent, endpoint)
    {
        ResponseContent = responseContent;
    }
}

/// <summary>
/// Exception thrown when NINA equipment is not found or not connected
/// </summary>
public class NinaEquipmentNotFoundException : NinaApiException
{
    public string? EquipmentType { get; }

    public NinaEquipmentNotFoundException(string equipmentType, string? endpoint = null) 
        : base($"NINA equipment not found or not connected: {equipmentType}", HttpStatusCode.NotFound, endpoint: endpoint)
    {
        EquipmentType = equipmentType;
    }
}

/// <summary>
/// Exception thrown when NINA equipment operation is not available or not supported
/// </summary>
public class NinaEquipmentNotAvailableException : NinaApiException
{
    public string? EquipmentType { get; }
    public string? Operation { get; }

    public NinaEquipmentNotAvailableException(string equipmentType, string operation, string? endpoint = null) 
        : base($"NINA equipment operation not available: {operation} on {equipmentType}", HttpStatusCode.BadRequest, endpoint: endpoint)
    {
        EquipmentType = equipmentType;
        Operation = operation;
    }
}

/// <summary>
/// Exception thrown when NINA API connection or communication fails
/// </summary>
public class NinaConnectionException : NinaApiException
{
    public NinaConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public NinaConnectionException(string message, string? endpoint = null) : base(message)
    {
        if (endpoint != null)
        {
            Data["Endpoint"] = endpoint;
        }
    }
}

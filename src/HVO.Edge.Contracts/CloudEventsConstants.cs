namespace HVO.Edge.Contracts;

/// <summary>
/// Standard CloudEvents 1.0 constant values.
/// </summary>
public static class CloudEventsConstants
{
    /// <summary>CloudEvents specification version.</summary>
    public const string SpecVersion = "1.0";

    /// <summary>JSON content type per RFC 8259.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>HTTP header name for W3C trace context.</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>HTTP header name for W3C trace state.</summary>
    public const string TraceStateHeader = "tracestate";

    /// <summary>Reverse-DNS prefix for all HVO CloudEvents types.</summary>
    public const string TypePrefix = "com.hvo.";
}

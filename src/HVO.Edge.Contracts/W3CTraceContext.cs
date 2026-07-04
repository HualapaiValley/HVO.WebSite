using System.Diagnostics;

namespace HVO.Edge.Contracts;

/// <summary>
/// Utilities for propagating W3C trace context to downstream services
/// via standard HTTP headers.
/// </summary>
public static class W3CTraceContext
{
    /// <summary>
    /// Adds W3C <c>traceparent</c> and <c>tracestate</c> headers to the
    /// request if there is an active <see cref="Activity"/> with a valid
    /// trace context.
    /// </summary>
    /// <returns>The request message for fluent chaining.</returns>
    public static HttpRequestMessage AddTraceContext(this HttpRequestMessage request)
    {
        var activity = Activity.Current;
        if (activity?.IdFormat == ActivityIdFormat.W3C && !string.IsNullOrEmpty(activity.Id))
        {
            request.Headers.TryAddWithoutValidation(CloudEventsConstants.TraceParentHeader, activity.Id);

            if (!string.IsNullOrEmpty(activity.TraceStateString))
                request.Headers.TryAddWithoutValidation(CloudEventsConstants.TraceStateHeader, activity.TraceStateString);
        }

        return request;
    }

    /// <summary>
    /// Creates a new <see cref="Activity"/> representing an outbox forward
    /// operation, linked to the current ambient activity if one exists.
    /// Callers should dispose the returned activity when the operation completes.
    /// </summary>
    public static Activity? StartForwardActivity(string gatewayName, string sourceId)
    {
        var activity = HvoActivitySource.Source.StartActivity(
            GatewayTelemetryConventions.OperationNames.OutboxForward,
            ActivityKind.Client);

        activity?
            .SetTag(GatewayTelemetryConventions.Tags.GatewayType, gatewayName)
            .SetTag(GatewayTelemetryConventions.Tags.SourceId, sourceId);

        return activity;
    }
}

/// <summary>
/// Shared <see cref="ActivitySource"/> for HVO edge telemetry.
/// </summary>
public static class HvoActivitySource
{
    public const string Name = "HVO.Edge";
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}

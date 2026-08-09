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
    public static Activity? StartForwardActivity(string gatewayType, string sourceId)
    {
        var activity = HvoActivitySource.Source.StartActivity(
            GatewayTelemetryConventions.OperationNames.OutboxForward,
            ActivityKind.Client);

        activity?
            .SetTag(GatewayTelemetryConventions.Tags.GatewayType, gatewayType)
            .SetTag(GatewayTelemetryConventions.Tags.SourceId, sourceId);

        return activity;
    }
}

/// <summary>
/// Shared <see cref="ActivitySource"/> for HVO edge telemetry.
/// </summary>
public static class HvoActivitySource
{
    public const string Name = GatewayTelemetryConventions.ActivitySourceName;
    public static readonly ActivitySource Source = new(Name, GatewayTelemetryConventions.Version);

    public static Activity? StartOperation(
        string operationName,
        ActivityKind kind,
        string gatewayId,
        string gatewayType,
        string? sourceId = null,
        string? deviceId = null,
        string? deviceType = null)
    {
        var activity = Source.StartActivity(operationName, kind);
        activity?.SetTag(GatewayTelemetryConventions.Tags.GatewayId, gatewayId)
            .SetTag(GatewayTelemetryConventions.Tags.GatewayType, gatewayType)
            .SetTag(GatewayTelemetryConventions.Tags.SourceId, sourceId)
            .SetTag(GatewayTelemetryConventions.Tags.DeviceId, deviceId)
            .SetTag(GatewayTelemetryConventions.Tags.DeviceType, deviceType);
        return activity;
    }

    public static Activity? StartCompletedOperation(
        string operationName,
        ActivityKind kind,
        TimeSpan duration,
        string gatewayId,
        string gatewayType,
        string? sourceId = null,
        string? deviceId = null,
        string? deviceType = null)
    {
        var parentContext = Activity.Current?.Context ?? default;
        var activity = Source.StartActivity(
            operationName,
            kind,
            parentContext,
            tags: null,
            links: null,
            startTime: DateTimeOffset.UtcNow - duration);
        activity?.SetTag(GatewayTelemetryConventions.Tags.GatewayId, gatewayId)
            .SetTag(GatewayTelemetryConventions.Tags.GatewayType, gatewayType)
            .SetTag(GatewayTelemetryConventions.Tags.SourceId, sourceId)
            .SetTag(GatewayTelemetryConventions.Tags.DeviceId, deviceId)
            .SetTag(GatewayTelemetryConventions.Tags.DeviceType, deviceType);
        return activity;
    }

    public static void Complete(Activity? activity, bool succeeded, string? failureKind = null)
    {
        if (activity is null)
            return;

        activity.SetTag(GatewayTelemetryConventions.Tags.Result,
            succeeded ? GatewayTelemetryConventions.Results.Success : GatewayTelemetryConventions.Results.Failure);
        if (!string.IsNullOrWhiteSpace(failureKind))
            activity.SetTag(GatewayTelemetryConventions.Tags.FailureKind, failureKind);
        activity.SetStatus(succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }
}

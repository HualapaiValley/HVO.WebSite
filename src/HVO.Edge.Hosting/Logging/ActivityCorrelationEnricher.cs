using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace HVO.Edge.Hosting.Logging;

internal sealed class ActivityCorrelationEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentId", activity.ParentSpanId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", activity.TraceId.ToHexString()));
    }
}

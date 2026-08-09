using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace HVO.Edge.Hosting.Logging;

public sealed class SanitizingLogEventSink(ILogger target) : ILogEventSink, IDisposable
{
    private static readonly MessageTemplateParser MessageTemplateParser = new();

    public void Emit(LogEvent logEvent) => target.Write(Sanitize(logEvent));

    public void Dispose()
    {
        if (target is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    internal static LogEvent Sanitize(LogEvent logEvent)
    {
        var properties = logEvent.Properties
            .Select(property => new LogEventProperty(
                property.Key,
                SensitivePropertyRedactionEnricher.SanitizeValue(property.Key, property.Value)))
            .ToList();

        if (logEvent.Exception is not null)
        {
            properties.Add(new LogEventProperty(
                "exception.type",
                new ScalarValue(logEvent.Exception.GetType().FullName)));
            properties.Add(new LogEventProperty(
                "exception.message",
                new ScalarValue(SensitivePropertyRedactionEnricher.SanitizeText(logEvent.Exception.Message))));

            if (!string.IsNullOrWhiteSpace(logEvent.Exception.StackTrace))
            {
                properties.Add(new LogEventProperty(
                    "exception.stacktrace",
                    new ScalarValue(SensitivePropertyRedactionEnricher.SanitizeText(logEvent.Exception.StackTrace))));
            }
        }

        var messageTemplate = MessageTemplateParser.Parse(
            SensitivePropertyRedactionEnricher.SanitizeText(logEvent.MessageTemplate.Text));

        return new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception: null,
            messageTemplate,
            properties,
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);
    }
}

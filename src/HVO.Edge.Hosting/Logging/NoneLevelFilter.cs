using Serilog.Core;
using Serilog.Events;

namespace HVO.Edge.Hosting.Logging;

internal sealed class NoneLevelFilter(
    bool defaultDisabled,
    IReadOnlyDictionary<string, bool> categoryDisabled) : ILogEventFilter
{
    public bool IsEnabled(LogEvent logEvent)
    {
        var sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var value) &&
            value is ScalarValue { Value: string source }
                ? source
                : null;

        if (sourceContext is null)
        {
            return !defaultDisabled;
        }

        var matchingCategory = categoryDisabled
            .Where(setting => sourceContext.Equals(setting.Key, StringComparison.Ordinal) ||
                sourceContext.StartsWith(setting.Key + ".", StringComparison.Ordinal))
            .OrderByDescending(setting => setting.Key.Length)
            .Select(setting => (bool?)setting.Value)
            .FirstOrDefault();

        return matchingCategory is null ? !defaultDisabled : !matchingCategory.Value;
    }
}

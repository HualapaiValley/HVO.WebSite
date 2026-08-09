using Serilog.Core;
using Serilog.Events;

namespace HVO.Edge.Hosting.Logging;

internal sealed class RepeatedFailureFilter(
    TimeSpan repeatInterval,
    TimeProvider? timeProvider = null) : ILogEventFilter
{
    private static readonly string[] IdentityPropertyNames =
    [
        "SourceId",
        "DeviceId",
        "Alias",
        "Address",
        "StatusCode",
        "PollResult",
        "FailureReason",
        "DegradedReason",
        "Error",
        "Reason",
        "Name",
        "Id",
        "NetworkName",
        "Cidr",
        "MacAddress",
        "hvo.source.id",
        "hvo.device.id"
    ];

    private const int MaxTrackedFailures = 1024;
    private readonly Dictionary<FailureFingerprint, DateTimeOffset> _lastEmitted = [];
    private readonly Lock _sync = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private int _eventsSinceCleanup;

    internal int TrackedFailureCount
    {
        get
        {
            lock (_sync)
            {
                return _lastEmitted.Count;
            }
        }
    }

    public bool IsEnabled(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning)
        {
            if (IsRecovery(logEvent))
            {
                lock (_sync)
                {
                    ResetMatchingFailures(logEvent);
                }
            }

            return true;
        }

        var fingerprint = new FailureFingerprint(
            GetScalar(logEvent, "SourceContext"),
            GetScalar(logEvent, "hvo.gateway.id"),
            GetIdentity(logEvent),
            logEvent.Level,
            logEvent.MessageTemplate.Text,
            logEvent.Exception?.GetType().FullName);
        var now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            CleanupIfNeeded(now);
            if (!_lastEmitted.TryGetValue(fingerprint, out var lastEmitted))
            {
                EnsureCapacity();
                _lastEmitted.Add(fingerprint, now);
                return true;
            }

            if (now - lastEmitted < repeatInterval)
            {
                return false;
            }

            _lastEmitted[fingerprint] = now;
            return true;
        }
    }

    private static string? GetScalar(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private static string GetIdentity(LogEvent logEvent) => string.Join('|', IdentityPropertyNames.Select(name =>
    {
        var value = GetScalar(logEvent, name);
        if (string.IsNullOrEmpty(value))
        {
            return $"{name}=";
        }

        var sanitized = SensitivePropertyRedactionEnricher.SanitizeText(value);
        return $"{name}={sanitized[..Math.Min(sanitized.Length, 128)]}";
    }));

    private static bool IsRecovery(LogEvent logEvent)
    {
        var template = logEvent.MessageTemplate.Text;
        if (template.Contains("recovered", StringComparison.OrdinalIgnoreCase) ||
            template.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
            template.Contains("established", StringComparison.OrdinalIgnoreCase) ||
            template.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
            template.Contains("queued", StringComparison.OrdinalIgnoreCase) ||
            template.Contains("forwarded", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return template.Contains("poll completed", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetScalar(logEvent, "PollResult"), "success", StringComparison.OrdinalIgnoreCase);
    }

    private void ResetMatchingFailures(LogEvent recoveryEvent)
    {
        var sourceContext = GetScalar(recoveryEvent, "SourceContext");
        var gatewayId = GetScalar(recoveryEvent, "hvo.gateway.id");
        var identity = GetIdentity(recoveryEvent);

        foreach (var fingerprint in _lastEmitted.Keys)
        {
            if (fingerprint.SourceContext == sourceContext &&
                fingerprint.GatewayId == gatewayId &&
                HasCompatibleIdentity(fingerprint.Identity, identity))
            {
                _lastEmitted.Remove(fingerprint);
            }
        }
    }

    private static bool HasCompatibleIdentity(string failureIdentity, string recoveryIdentity)
    {
        var failureParts = failureIdentity.Split('|');
        var recoveryParts = recoveryIdentity.Split('|');
        for (var index = 0; index < failureParts.Length; index++)
        {
            var failureValue = failureParts[index].Split('=', 2)[1];
            var recoveryValue = recoveryParts[index].Split('=', 2)[1];
            if (failureValue.Length > 0 && recoveryValue.Length > 0 && failureValue != recoveryValue)
            {
                return false;
            }
        }

        return true;
    }

    private void CleanupIfNeeded(DateTimeOffset now)
    {
        _eventsSinceCleanup++;
        if (_eventsSinceCleanup < 256)
        {
            return;
        }

        _eventsSinceCleanup = 0;
        var expiresBefore = now - repeatInterval - repeatInterval;
        foreach (var fingerprint in _lastEmitted
            .Where(entry => entry.Value < expiresBefore)
            .Select(entry => entry.Key)
            .ToList())
        {
            _lastEmitted.Remove(fingerprint);
        }
    }

    private void EnsureCapacity()
    {
        if (_lastEmitted.Count < MaxTrackedFailures)
        {
            return;
        }

        var oldest = _lastEmitted.MinBy(entry => entry.Value).Key;
        _lastEmitted.Remove(oldest);
    }

    private sealed record FailureFingerprint(
        string? SourceContext,
        string? GatewayId,
        string Identity,
        LogEventLevel Level,
        string MessageTemplate,
        string? ExceptionType);
}

using Serilog.Core;
using Serilog.Events;
using System.Text.RegularExpressions;

namespace HVO.Edge.Hosting.Logging;

public sealed class SensitivePropertyRedactionEnricher : ILogEventEnricher
{
    private static readonly string[] SensitiveNameParts =
    [
        "apikey",
        "authorization",
        "connectionstring",
        "cookie",
        "credential",
        "password",
        "payload",
        "privatekey",
        "protocolframe",
        "rawframe",
        "requestbody",
        "responsebody",
        "secret",
        "token"
    ];

    private static readonly LogEventPropertyValue RedactedValue = new ScalarValue("[REDACTED]");
    private static readonly Regex SensitiveValuePattern = new(
        @"(?i)\b(api[-_.]?key|authorization|credential|password|private[-_.]?key|secret|token)\s*[:=]\s*(?:(?:bearer|basic)\s+)?[^\s,;&]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AuthorizationSchemePattern = new(
        @"(?i)\b(bearer|basic)\s+[^\s,;&]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UriUserInfoPattern = new(
        @"(?i)\b(https?://)[^/@\s]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(
                property.Key,
                SanitizeValue(property.Key, property.Value)));
        }
    }

    internal static LogEventPropertyValue SanitizeValue(string? propertyName, LogEventPropertyValue value)
    {
        if (IsSensitiveName(propertyName))
        {
            return RedactedValue;
        }

        return value switch
        {
            ScalarValue { Value: string text } => new ScalarValue(SanitizeText(text)),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(element => SanitizeValue(null, element))),
            StructureValue structure => new StructureValue(
                structure.Properties.Select(property =>
                    new LogEventProperty(property.Name, SanitizeValue(property.Name, property.Value))),
                structure.TypeTag),
            DictionaryValue dictionary => new DictionaryValue(
                dictionary.Elements.Select(element => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    element.Key,
                    SanitizeValue(element.Key.Value?.ToString(), element.Value)))),
            _ => value
        };
    }

    internal static string SanitizeText(string text)
    {
        var sanitized = SensitiveValuePattern.Replace(text, "$1=[REDACTED]");
        sanitized = AuthorizationSchemePattern.Replace(sanitized, "$1 [REDACTED]");
        sanitized = UriUserInfoPattern.Replace(sanitized, "$1[REDACTED]@");
        if (!Uri.TryCreate(sanitized, UriKind.Absolute, out var uri) ||
            (string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)))
        {
            return sanitized;
        }

        return new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty
        }.Uri.ToString();
    }

    private static bool IsSensitiveName(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var normalizedName = new string(propertyName.Where(char.IsLetterOrDigit).ToArray());
        return SensitiveNameParts.Any(part => normalizedName.Contains(part, StringComparison.OrdinalIgnoreCase));
    }
}

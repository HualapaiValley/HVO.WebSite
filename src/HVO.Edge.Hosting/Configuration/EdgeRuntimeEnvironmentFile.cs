using Microsoft.Extensions.Configuration;

namespace HVO.Edge.Hosting.Configuration;

/// <summary>Reads non-secret startup overrides without executing dotenv expressions.</summary>
internal static class EdgeRuntimeEnvironmentFile
{
    internal const string EnvironmentVariable = "HVO_EDGE_RUNTIME_ENV_FILE";

    internal static IReadOnlyDictionary<string, string?> Read(string path, string configRoot, string? defaultServiceName)
    {
        if (!Path.IsPathFullyQualified(path) || !EdgePathOptionsValidator.IsWithin(path, configRoot))
            throw new InvalidOperationException($"{EnvironmentVariable} must select an absolute file under Edge:Paths:ConfigDirectory.");

        try
        {
            // INI's flat KEY=value/quoted-value syntax is the supported dotenv subset.
            // No shell expansion, interpolation or automatic reload is performed.
            using var file = new ConfigurationManager();
            using var stream = File.OpenRead(path);
            file.AddIniStream(stream);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://192.168.1.238:4318",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["OTEL_SERVICE_NAME"] = defaultServiceName
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in file.AsEnumerable())
            {
                var key = item.Key;
                if (key.Contains(':'))
                    throw new FormatException(); // Sections belong in gateway.json, not this flat file.

                if (key.Equals("DAVIS_STATION_HOST", StringComparison.OrdinalIgnoreCase))
                    key = "Station:Host";
                else if (key.Contains("__", StringComparison.Ordinal))
                    key = key.Replace("__", ":", StringComparison.Ordinal);
                else if (!key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase))
                    continue; // Image pins, paths, ports and overlay switches belong to Compose.

                if (!seen.Add(key))
                    throw new FormatException();
                if (values.ContainsKey(key) && string.IsNullOrWhiteSpace(item.Value))
                    continue; // Keep the same unset/empty OTLP defaults as Compose.
                values[key] = item.Value;
            }
            return values;
        }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        {
            // Parser messages can contain the invalid line/value. Do not expose it.
            throw new InvalidOperationException("Cannot read runtime .env; require a readable, flat KEY=value file with unique setting names.");
        }
    }
}

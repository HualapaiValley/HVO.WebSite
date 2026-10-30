using Microsoft.Extensions.Configuration;

namespace HVO.Edge.Hosting.Configuration;

/// <summary>Reads non-secret startup overrides without executing dotenv expressions.</summary>
internal static class EdgeRuntimeEnvironmentFile
{
    internal const string EnvironmentVariable = "HVO_EDGE_RUNTIME_ENV_FILE";

    internal static IReadOnlyDictionary<string, string?> Read(string path, string configRoot)
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
            // Deployment defaults belong to Compose/env examples. Only return
            // settings actually present here, including explicitly empty values.
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in file.AsEnumerable())
            {
                var key = item.Key;
                if (key.Contains(':'))
                    throw new FormatException(); // Sections belong in gateway.json, not this flat file.

                // Ordinary Compose secret-directory paths are metadata and ignored.
                // App overrides and all other credential-like keys fail closed.
                var composeSecretsDirectory = !key.Contains("__", StringComparison.Ordinal)
                    && key.EndsWith("_SECRETS_DIRECTORY", StringComparison.OrdinalIgnoreCase);
                if (!composeSecretsDirectory && IsSecretLike(key))
                    throw new FormatException();

                if (key.Equals("DAVIS_STATION_HOST", StringComparison.OrdinalIgnoreCase))
                    key = "Station:Host";
                else if (key.Contains("__", StringComparison.Ordinal))
                    key = key.Replace("__", ":", StringComparison.Ordinal);
                else if (!key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase))
                    continue; // Image pins, paths, ports and overlay switches belong to Compose.

                if (!seen.Add(key))
                    throw new FormatException();
                values[key] = item.Value;
            }
            return values;
        }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        {
            // Parser messages can contain the invalid line/value. Do not expose it.
            throw new InvalidOperationException("Cannot read runtime .env; require a readable, flat KEY=value file with unique non-secret setting names.");
        }
    }

    private static bool IsSecretLike(string key)
    {
        // Normalize separators as well as case, so API_KEY and ApiKey are equivalent.
        var normalized = string.Concat(key.Where(char.IsLetterOrDigit));
        return new[] { "Password", "Token", "Secret", "ApiKey", "ConnectionString" }
            .Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

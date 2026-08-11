using HVO.Edge.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Configuration;

public sealed class StationOptionsValidator(EdgeRuntimeIdentity identity) : IValidateOptions<StationOptions>
{
    public ValidateOptionsResult Validate(string? name, StationOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Host) || options.Host != options.Host.Trim())
            failures.Add("Station:Host is required and must be trimmed.");
        if (string.IsNullOrWhiteSpace(options.StationId) || options.StationId != options.StationId.Trim() || options.StationId.Length > 64)
            failures.Add("Station:StationId must be a trimmed value of 1-64 characters.");
        if (!string.Equals(identity.SourceId, options.StationId, StringComparison.Ordinal))
            failures.Add("Station:StationId must match Edge:Runtime:SourceId.");
        if (string.IsNullOrWhiteSpace(identity.SiteId))
            failures.Add("Edge:Runtime:SiteId is required for Davis Home Assistant identity.");
        if (!Uri.TryCreate(options.CentralIngestBaseEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            failures.Add("Station:CentralIngestBaseEndpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.");
        else if (endpoint.Scheme != "https" && !options.AllowInsecureCentralIngest)
            failures.Add("Station:CentralIngestBaseEndpoint must use HTTPS unless insecure ingest is explicitly enabled.");
        if (string.IsNullOrWhiteSpace(options.CentralApiKeySecret))
            failures.Add("Station:CentralApiKeySecret is required.");
        if (options.LegacyArchiveConsoleUtcOffsetHours is null or < -12 or > 14)
            failures.Add("Station:LegacyArchiveConsoleUtcOffsetHours must be configured between -12 and 14 for deterministic legacy migration.");
        if (!Path.IsPathFullyQualified(options.LocalDatabasePath) || string.IsNullOrWhiteSpace(Path.GetFileName(options.LocalDatabasePath)))
            failures.Add("Station:LocalDatabasePath must be an absolute database file path.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

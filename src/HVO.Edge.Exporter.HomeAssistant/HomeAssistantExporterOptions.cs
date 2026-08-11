using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

public sealed class HomeAssistantExporterOptions
{
    public const string SectionName = "HomeAssistant:Exporter";
    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string AccessTokenSecret { get; set; } = "home-assistant-token";
    public string? CentralIngestEndpoint { get; set; }
    public string CentralApiKeySecret { get; set; } = "central-ingest-api-key";
    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 15;
    public int InitialReconnectDelaySeconds { get; set; } = 1;
    public int MaxReconnectDelaySeconds { get; set; } = 30;
    public int MaxMessageBytes { get; set; } = 8 * 1024 * 1024;
    public int RetryExhaustedRequeueMinutes { get; set; } = 15;
    public bool AllowInsecureCentralIngest { get; set; }
    public bool AllowTestPlatforms { get; set; }
    public List<HomeAssistantExportMapping> Mappings { get; set; } = [];
}

public sealed class HomeAssistantExportMapping
{
    public string? Id { get; set; }
    public HomeAssistantExportContract Contract { get; set; }
    public string? SourceId { get; set; }
    public string? DeviceId { get; set; }
    public string? ExpectedPlatform { get; set; }
    public List<HomeAssistantEntityBinding> Entities { get; set; } = [];
}

public sealed class HomeAssistantEntityBinding
{
    public string? EntityId { get; set; }
    public HomeAssistantMetric Metric { get; set; }
    public bool Required { get; set; } = true;
}

public enum HomeAssistantExportContract { PowerReading, WeatherRaw }
public enum HomeAssistantMetric { LoadPowerW, GridVoltageV, Temperature, HumidityPercent }

internal sealed class HomeAssistantExporterOptionsValidator : IValidateOptions<HomeAssistantExporterOptions>
{
    public ValidateOptionsResult Validate(string? name, HomeAssistantExporterOptions options)
    {
        if (!Uri.TryCreate(options.CentralIngestEndpoint, UriKind.Absolute, out var ingest)
            || ingest.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(ingest.UserInfo)
            || !string.IsNullOrEmpty(ingest.Query) || !string.IsNullOrEmpty(ingest.Fragment))
            return ValidateOptionsResult.Fail("HomeAssistant:Exporter:CentralIngestEndpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.");
        if (ingest.Scheme != "https" && !options.AllowInsecureCentralIngest)
            return ValidateOptionsResult.Fail("HomeAssistant:Exporter:CentralIngestEndpoint must use HTTPS unless insecure ingest is explicitly enabled for testing.");
        if (string.IsNullOrWhiteSpace(options.CentralApiKeySecret))
            return ValidateOptionsResult.Fail("The central ingest secret file name is required.");
        if (options.RetryExhaustedRequeueMinutes is < 1 or > 1440)
            return ValidateOptionsResult.Fail("The retry-exhausted requeue interval is outside supported bounds.");
        if (!options.Enabled)
            return ValidateOptionsResult.Success;
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("ws" or "wss")
            || endpoint.AbsolutePath != "/api/websocket"
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
            return ValidateOptionsResult.Fail("HomeAssistant:Exporter:Endpoint must be an absolute ws/wss /api/websocket URI without credentials, query, or fragment.");
        if (string.IsNullOrWhiteSpace(options.AccessTokenSecret))
            return ValidateOptionsResult.Fail("The Home Assistant access-token secret file name is required.");
        if (options.ConnectTimeoutSeconds is < 1 or > 120
            || options.CommandTimeoutSeconds is < 1 or > 120
            || options.InitialReconnectDelaySeconds is < 1 or > 60
            || options.MaxReconnectDelaySeconds < options.InitialReconnectDelaySeconds
            || options.MaxReconnectDelaySeconds > 600
            || options.MaxMessageBytes is < 65536 or > 33554432)
            return ValidateOptionsResult.Fail("Home Assistant exporter timeout, reconnect, or message-size settings are outside supported bounds.");
        if (options.Mappings.Count == 0)
            return ValidateOptionsResult.Fail("At least one Home Assistant export mapping is required.");

        var mappingIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var entityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in options.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Id) || !mappingIds.Add(mapping.Id))
                return ValidateOptionsResult.Fail("Mapping IDs must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(mapping.SourceId) || mapping.SourceId.Length > 64 || !sourceIds.Add(mapping.SourceId))
                return ValidateOptionsResult.Fail("Mapping source IDs must be non-empty, unique, and no longer than 64 characters.");
            if (string.IsNullOrWhiteSpace(mapping.DeviceId) || mapping.DeviceId.Length > 64)
                return ValidateOptionsResult.Fail($"Mapping {mapping.Id} requires a device ID no longer than 64 characters.");
            if (string.IsNullOrWhiteSpace(mapping.ExpectedPlatform)
                || string.Equals(mapping.ExpectedPlatform, "mqtt", StringComparison.OrdinalIgnoreCase))
                return ValidateOptionsResult.Fail($"Mapping {mapping.Id} requires a non-MQTT expected platform.");
            var allowedPlatform = mapping.Contract switch
            {
                HomeAssistantExportContract.PowerReading => mapping.ExpectedPlatform == "tplink",
                HomeAssistantExportContract.WeatherRaw => mapping.ExpectedPlatform == "govee_ble",
                _ => false
            };
            if (!allowedPlatform && !(options.AllowTestPlatforms && mapping.ExpectedPlatform == "template"))
                return ValidateOptionsResult.Fail($"Mapping {mapping.Id} does not use an approved platform for {mapping.Contract}.");
            var requiredPrefix = mapping.Contract == HomeAssistantExportContract.PowerReading ? "kasa:" : "govee:";
            if (!mapping.SourceId.StartsWith(requiredPrefix, StringComparison.Ordinal))
                return ValidateOptionsResult.Fail($"Mapping {mapping.Id} source ID must use the reserved {requiredPrefix} prefix.");
            if (mapping.Entities.Count == 0)
                return ValidateOptionsResult.Fail($"Mapping {mapping.Id} requires entity bindings.");

            var metrics = new HashSet<HomeAssistantMetric>();
            foreach (var binding in mapping.Entities)
            {
                if (string.IsNullOrWhiteSpace(binding.EntityId)
                    || !binding.EntityId.StartsWith("sensor.", StringComparison.Ordinal)
                    || binding.EntityId.StartsWith("sensor.hvo_", StringComparison.Ordinal)
                    || !entityIds.Add(binding.EntityId))
                    return ValidateOptionsResult.Fail("Entity IDs must be unique sensor IDs and cannot target HVO-owned MQTT entities.");
                if (!metrics.Add(binding.Metric))
                    return ValidateOptionsResult.Fail($"Mapping {mapping.Id} contains duplicate metrics.");
            }

            var valid = mapping.Contract switch
            {
                HomeAssistantExportContract.PowerReading => metrics.Contains(HomeAssistantMetric.LoadPowerW)
                    && metrics.All(static metric => metric is HomeAssistantMetric.LoadPowerW or HomeAssistantMetric.GridVoltageV),
                HomeAssistantExportContract.WeatherRaw => metrics.Contains(HomeAssistantMetric.Temperature)
                    && metrics.Contains(HomeAssistantMetric.HumidityPercent)
                    && metrics.All(static metric => metric is HomeAssistantMetric.Temperature or HomeAssistantMetric.HumidityPercent),
                _ => false
            };
            if (!valid)
                return ValidateOptionsResult.Fail($"Mapping {mapping.Id} has metrics incompatible with {mapping.Contract}.");
            var mandatory = mapping.Contract == HomeAssistantExportContract.PowerReading
                ? new[] { HomeAssistantMetric.LoadPowerW }
                : new[] { HomeAssistantMetric.Temperature, HomeAssistantMetric.HumidityPercent };
            if (mapping.Entities.Any(binding => mandatory.Contains(binding.Metric) && !binding.Required))
                return ValidateOptionsResult.Fail($"Mapping {mapping.Id} marks a mandatory metric as optional.");
        }
        return ValidateOptionsResult.Success;
    }
}

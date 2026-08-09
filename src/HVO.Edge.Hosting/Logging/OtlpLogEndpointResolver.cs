using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog.Sinks.OpenTelemetry;

namespace HVO.Edge.Hosting.Logging;

internal sealed record OtlpLogExportSettings(
    string Endpoint,
    OtlpProtocol Protocol,
    IReadOnlyDictionary<string, string>? Headers = null);

internal static class OtlpLogEndpointResolver
{
    private const string HttpProtobuf = "http/protobuf";
    private const string Grpc = "grpc";

    public static OtlpLogExportSettings? Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing") &&
            !string.Equals(configuration["HVO_LOGGING_ENABLE_OTLP_IN_TESTS"], "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var logsEndpoint = configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];
        var baseEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var endpoint = !string.IsNullOrWhiteSpace(logsEndpoint) ? logsEndpoint : baseEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var protocolValue = configuration["OTEL_EXPORTER_OTLP_LOGS_PROTOCOL"]
            ?? configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]
            ?? HttpProtobuf;

        var protocol = protocolValue.Trim().ToLowerInvariant() switch
        {
            HttpProtobuf => OtlpProtocol.HttpProtobuf,
            Grpc => OtlpProtocol.Grpc,
            _ => (OtlpProtocol?)null
        };

        if (protocol is null)
        {
            return null;
        }

        var resolvedEndpoint = endpointUri.ToString().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(logsEndpoint) &&
            protocol == OtlpProtocol.HttpProtobuf &&
            !resolvedEndpoint.EndsWith("/v1/logs", StringComparison.OrdinalIgnoreCase))
        {
            resolvedEndpoint += "/v1/logs";
        }

        var headers = ParseHeaders(
            configuration["OTEL_EXPORTER_OTLP_LOGS_HEADERS"]
            ?? configuration["OTEL_EXPORTER_OTLP_HEADERS"]);
        return new OtlpLogExportSettings(resolvedEndpoint, protocol.Value, headers);
    }

    private static IReadOnlyDictionary<string, string> ParseHeaders(string? configuredHeaders)
    {
        if (string.IsNullOrWhiteSpace(configuredHeaders))
        {
            return new Dictionary<string, string>();
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuredHeaders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(entry[..separator].Trim());
            var value = Uri.UnescapeDataString(entry[(separator + 1)..].Trim());
            if (name.Length > 0 && value.Length > 0)
            {
                headers[name] = value;
            }
        }

        return headers;
    }
}

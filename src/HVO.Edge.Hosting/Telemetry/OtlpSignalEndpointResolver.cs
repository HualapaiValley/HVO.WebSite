using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;

namespace HVO.Edge.Hosting.Telemetry;

internal sealed record OtlpSignalExportSettings(Uri Endpoint, OtlpExportProtocol Protocol);

internal static class OtlpSignalEndpointResolver
{
    private const string HttpProtobuf = "http/protobuf";
    private const string Grpc = "grpc";

    public static OtlpSignalExportSettings? Resolve(
        IConfiguration configuration,
        string signalName)
    {
        var normalizedSignalName = signalName.ToUpperInvariant();
        var signalEndpoint = configuration[$"OTEL_EXPORTER_OTLP_{normalizedSignalName}_ENDPOINT"];
        var endpoint = !string.IsNullOrWhiteSpace(signalEndpoint)
            ? signalEndpoint
            : configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri)
            || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var protocolValue = configuration[$"OTEL_EXPORTER_OTLP_{normalizedSignalName}_PROTOCOL"]
            ?? configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]
            ?? HttpProtobuf;
        var protocol = protocolValue.Trim().ToLowerInvariant() switch
        {
            HttpProtobuf => OtlpExportProtocol.HttpProtobuf,
            Grpc => OtlpExportProtocol.Grpc,
            _ => (OtlpExportProtocol?)null,
        };
        if (protocol is null)
            return null;

        if (string.IsNullOrWhiteSpace(signalEndpoint) && protocol == OtlpExportProtocol.HttpProtobuf)
        {
            endpointUri = new Uri($"{endpointUri.ToString().TrimEnd('/')}/v1/{signalName}");
        }

        return new OtlpSignalExportSettings(endpointUri, protocol.Value);
    }
}

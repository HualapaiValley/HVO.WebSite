using FluentAssertions;
using HVO.Edge.Hosting.Telemetry;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;

namespace HVO.Edge.Hosting.Tests.Telemetry;

[TestClass]
public sealed class OtlpSignalEndpointResolverTests
{
    [TestMethod]
    [DataRow("traces", "http://collector:4318/v1/traces")]
    [DataRow("metrics", "http://collector:4318/v1/metrics")]
    public void Resolve_CommonHttpEndpoint_AppendsSignalPath(string signalName, string expectedEndpoint)
    {
        var settings = Resolve(signalName, new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318/",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        });

        settings.Should().NotBeNull();
        settings!.Endpoint.Should().Be(expectedEndpoint);
        settings.Protocol.Should().Be(OtlpExportProtocol.HttpProtobuf);
    }

    [TestMethod]
    [DataRow("traces", "http://collector:4318/v1/traces")]
    [DataRow("metrics", "http://collector:4318/V1/METRICS/")]
    public void Resolve_CommonHttpEndpoint_WithSignalPath_DoesNotAppendItAgain(
        string signalName,
        string endpoint)
    {
        var settings = Resolve(signalName, new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        });

        settings!.Endpoint.OriginalString.Should().Be(endpoint);
    }

    [TestMethod]
    [DataRow("traces", "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")]
    [DataRow("metrics", "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")]
    public void Resolve_SignalSpecificHttpEndpoint_UsesEndpointUnchanged(string signalName, string settingName)
    {
        const string signalEndpoint = "https://collector.example.test/custom";
        var settings = Resolve(signalName, new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318",
            [settingName] = signalEndpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        });

        settings!.Endpoint.OriginalString.Should().Be(signalEndpoint);
    }

    [TestMethod]
    public void Resolve_GrpcEndpoint_DoesNotAppendHttpSignalPath()
    {
        var settings = Resolve("traces", new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
        });

        settings!.Endpoint.OriginalString.Should().Be("http://collector:4317");
        settings.Protocol.Should().Be(OtlpExportProtocol.Grpc);
    }

    [TestMethod]
    [DataRow(null, "http/protobuf")]
    [DataRow("", "http/protobuf")]
    [DataRow("not-a-uri", "http/protobuf")]
    [DataRow("ftp://collector:4318", "http/protobuf")]
    [DataRow("http://collector:4318", "invalid")]
    public void Resolve_EmptyOrInvalidConfiguration_DisablesExport(string? endpoint, string protocol)
    {
        var settings = Resolve("metrics", new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = protocol,
        });

        settings.Should().BeNull();
    }

    private static OtlpSignalExportSettings? Resolve(
        string signalName,
        Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return OtlpSignalEndpointResolver.Resolve(configuration, signalName);
    }
}

using FluentAssertions;
using HVO.Edge.Hosting.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog.Sinks.OpenTelemetry;

namespace HVO.Edge.Hosting.Tests.Logging;

[TestClass]
public sealed class OtlpLogEndpointResolverTests
{
    [TestMethod]
    public void Resolve_GenericHttpEndpoint_AppendsLogsSignalPath()
    {
        var settings = Resolve(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318/",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
        });

        settings!.Endpoint.Should().Be("http://collector:4318/v1/logs");
        settings.Protocol.Should().Be(OtlpProtocol.HttpProtobuf);
    }

    [TestMethod]
    public void Resolve_SignalSpecificEndpoint_UsesEndpointUnchanged()
    {
        var settings = Resolve(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318",
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = "https://logs.example.test/custom/v1/logs",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
        });

        settings!.Endpoint.Should().Be("https://logs.example.test/custom/v1/logs");
    }

    [TestMethod]
    public void Resolve_GrpcEndpoint_DoesNotAppendHttpSignalPath()
    {
        var settings = Resolve(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc"
        });

        settings!.Endpoint.Should().Be("http://collector:4317");
        settings.Protocol.Should().Be(OtlpProtocol.Grpc);
    }

    [TestMethod]
    public void Resolve_TestingEnvironment_DisablesExportByDefault()
    {
        var settings = Resolve(
            new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318" },
            "Testing");

        settings.Should().BeNull();
    }

    [TestMethod]
    public void Resolve_TestingEnvironment_AllowsExplicitOptIn()
    {
        var settings = Resolve(
            new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318",
                ["HVO_LOGGING_ENABLE_OTLP_IN_TESTS"] = "true"
            },
            "Testing");

        settings.Should().NotBeNull();
    }

    [TestMethod]
    public void Resolve_LogsHeadersOverrideGenericHeaders()
    {
        var settings = Resolve(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "Authorization=generic",
            ["OTEL_EXPORTER_OTLP_LOGS_HEADERS"] = "Authorization=Bearer%20logs-token,X-Tenant=hvo"
        });

        settings!.Headers.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer logs-token",
            ["X-Tenant"] = "hvo"
        });
    }

    [TestMethod]
    [DataRow("not-a-uri", "http/protobuf")]
    [DataRow("ftp://collector/logs", "http/protobuf")]
    [DataRow("http://collector:4318", "invalid")]
    public void Resolve_InvalidConfiguration_DisablesExport(string endpoint, string protocol)
    {
        var settings = Resolve(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = protocol
        });

        settings.Should().BeNull();
    }

    private static OtlpLogExportSettings? Resolve(
        Dictionary<string, string?> values,
        string environmentName = "Production")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return OtlpLogEndpointResolver.Resolve(configuration, new TestHostEnvironment
        {
            EnvironmentName = environmentName
        });
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "HVO.Edge.Hosting.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

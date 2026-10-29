using FluentAssertions;
using HVO.Edge.Hosting.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace HVO.Edge.Hosting.Tests.Configuration;

[TestClass]
public sealed class EdgeRuntimeEnvironmentFileTests
{
    [TestMethod]
    public void StartupFile_OverridesCreationEnvironmentAndMountedJson_WithTypedAndIndexedKeys()
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, """
                # Non-secret application overrides
                OTEL_EXPORTER_OTLP_ENDPOINT=http://fixture:4317
                OTEL_EXPORTER_OTLP_PROTOCOL=grpc
                OTEL_SERVICE_NAME="fixture-collector"
                DAVIS_STATION_HOST=fixture-console
                Outbox__BatchSize=12
                JkBms__Devices__0__Alias=fixture-bank
                FIXTURE_HTTP_PORT=5999
                """);
            var builder = Builder(root, path);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_SERVICE_NAME"] = "creation-time",
                ["Outbox:BatchSize"] = "50"
            });

            builder.AddHvoEdgeMountedConfiguration();

            builder.Configuration["OTEL_SERVICE_NAME"].Should().Be("fixture-collector");
            builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"].Should().Be("grpc");
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"].Should().Be("http://fixture:4317");
            builder.Configuration["Station:Host"].Should().Be("fixture-console");
            builder.Configuration.GetValue<int>("Outbox:BatchSize").Should().Be(12);
            builder.Configuration["JkBms:Devices:0:Alias"].Should().Be("fixture-bank");
            builder.Configuration["FIXTURE_HTTP_PORT"].Should().BeNull();
        });
    }

    [TestMethod]
    public void StartupFile_RereadsAtomicallyReplacedFileOnlyOnNextStartup()
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, "OTEL_SERVICE_NAME=before\nOutbox__BatchSize=12\n");
            var first = Builder(root, path).AddHvoEdgeMountedConfiguration();
            var replacement = Path.Combine(root, ".env.next");
            File.WriteAllText(replacement, "OTEL_SERVICE_NAME=after\nOutbox__BatchSize=24\n");
            File.Move(replacement, path, overwrite: true);

            first.Configuration["OTEL_SERVICE_NAME"].Should().Be("before");
            first.Configuration.GetValue<int>("Outbox:BatchSize").Should().Be(12);
            var restarted = Builder(root, path).AddHvoEdgeMountedConfiguration();
            restarted.Configuration["OTEL_SERVICE_NAME"].Should().Be("after");
            restarted.Configuration.GetValue<int>("Outbox:BatchSize").Should().Be(24);
        });
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("OTEL_EXPORTER_OTLP_ENDPOINT=\nOTEL_EXPORTER_OTLP_PROTOCOL=\nOTEL_SERVICE_NAME=\n")]
    public void StartupFile_UnsetOrEmptyTelemetrySettingsUseComposeDefaults(string contents)
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, contents);
            var builder = Builder(root, path).AddHvoEdgeMountedConfiguration();

            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"].Should().Be("http://192.168.1.238:4318");
            builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"].Should().Be("http/protobuf");
            builder.Configuration["OTEL_SERVICE_NAME"].Should().Be("fixture-default");
        });
    }

    [TestMethod]
    [DataRow("invalid fixture-value-must-not-leak")]
    [DataRow("OTEL_SERVICE_NAME=first\nOTEL_SERVICE_NAME=fixture-value-must-not-leak")]
    [DataRow("[Section]\nKey=fixture-value-must-not-leak")]
    [DataRow("DAVIS_STATION_HOST=first\nStation__Host=fixture-value-must-not-leak")]
    public void StartupFile_InvalidSyntaxOrDuplicateSettingsFailWithoutPrintingContents(string contents)
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, contents);
            var read = () => Builder(root, path).AddHvoEdgeMountedConfiguration();

            var error = read.Should().Throw<InvalidOperationException>().Which;
            error.ToString().Should().NotContain("fixture-value-must-not-leak");
            error.Message.Should().Contain("Cannot read runtime .env");
        });
    }

    [TestMethod]
    public void StartupFile_SelectedMissingFileFailsClosed()
    {
        WithDirectory(root =>
        {
            var read = () => Builder(root, Path.Combine(root, "missing.env")).AddHvoEdgeMountedConfiguration();
            read.Should().Throw<InvalidOperationException>().WithMessage("Cannot read runtime .env*");
        });
    }

    [TestMethod]
    public void StartupFile_RejectsOutsideConfigRoot()
    {
        WithDirectory(root =>
        {
            var read = () => Builder(root, Path.Combine(root, "..", "outside.env")).AddHvoEdgeMountedConfiguration();
            read.Should().Throw<InvalidOperationException>().WithMessage("*must select an absolute file under*");
        });
    }

    private static WebApplicationBuilder Builder(string root, string runtimeFile)
    {
        var json = Path.Combine(root, "gateway.json");
        File.WriteAllText(json, """{"Edge":{"Runtime":{"ServiceName":"fixture-default"}},"Outbox":{"BatchSize":50}}""");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Edge:Paths:ConfigDirectory"] = root,
            [EdgeConfigurationBuilderExtensions.ConfigurationFileEnvironmentVariable] = json,
            [EdgeRuntimeEnvironmentFile.EnvironmentVariable] = runtimeFile
        });
        return builder;
    }

    private static void WithDirectory(Action<string> test)
    {
        var directory = Directory.CreateTempSubdirectory("hvo-runtime-env-");
        try { test(directory.FullName); }
        finally { directory.Delete(recursive: true); }
    }
}

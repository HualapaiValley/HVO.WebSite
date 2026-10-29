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
    public void StartupFile_UnsetTelemetryDoesNotInventDeploymentDefaults()
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, "Outbox__BatchSize=12\n");
            var overrides = EdgeRuntimeEnvironmentFile.Read(path, root);

            overrides.Should().ContainSingle().Which.Key.Should().Be("Outbox:BatchSize");
            overrides.Keys.Should().NotContain(key => key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void StartupFile_ExplicitlyEmptyTelemetryValuesRemainEmpty()
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, "OTEL_EXPORTER_OTLP_ENDPOINT=\nOTEL_EXPORTER_OTLP_PROTOCOL=\nOTEL_SERVICE_NAME=\n");
            var overrides = EdgeRuntimeEnvironmentFile.Read(path, root);

            overrides.Should().HaveCount(3);
            overrides.Values.Should().OnlyContain(value => value == string.Empty);
        });
    }

    [TestMethod]
    [DoNotParallelize]
    public void StartupFile_IntentionallyWinsOverRealEnvironmentWhichWinsOverJson()
    {
        const string key = "HvoFixturePrecedence__Value";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            WithDirectory(root =>
            {
                var path = Path.Combine(root, ".env");
                File.WriteAllText(path, $"{key}=startup-file\n");
                Environment.SetEnvironmentVariable(key, "creation-environment");
                var startup = Builder(root, path).AddHvoEdgeMountedConfiguration();
                startup.Configuration["HvoFixturePrecedence:Value"].Should().Be("startup-file");

                File.WriteAllText(path, string.Empty);
                var withoutFileOverride = Builder(root, path).AddHvoEdgeMountedConfiguration();
                withoutFileOverride.Configuration["HvoFixturePrecedence:Value"].Should().Be("creation-environment");

                Environment.SetEnvironmentVariable(key, null);
                var withoutEnvironmentOverride = Builder(root, path).AddHvoEdgeMountedConfiguration();
                withoutEnvironmentOverride.Configuration["HvoFixturePrecedence:Value"].Should().Be("mounted-json");
            });
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    [TestMethod]
    [DataRow("HomeAssistant__Mqtt__Password")]
    [DataRow("service__ACCESS_TOKEN")]
    [DataRow("Station__CentralApiKeySecret")]
    [DataRow("service__api_key")]
    [DataRow("ConnectionStrings__Hvo")]
    [DataRow("PASSWORD")]
    [DataRow("DATABASE_CONNECTION_STRING")]
    [DataRow("ApiKey")]
    [DataRow("OTEL_ACCESS_TOKEN")]
    [DataRow("Fixture__sEcReT")]
    [DataRow("Edge__Paths__SecretsDirectory")]
    public void StartupFile_RejectsSecretLikeKeysBeforeIgnoringUnknownNames_WithoutLeakingNamesOrValues(string key)
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, $"{key}=fixture-value-must-not-leak\n");
            var read = () => Builder(root, path).AddHvoEdgeMountedConfiguration();

            var error = read.Should().Throw<InvalidOperationException>().Which;
            error.ToString().Should().NotContain(key).And.NotContain("fixture-value-must-not-leak");
            error.InnerException.Should().BeNull();
            error.Message.Should().Be("Cannot read runtime .env; require a readable, flat KEY=value file with unique non-secret setting names.");
        });
    }

    [TestMethod]
    public void StartupFile_IgnoresComposeSecretDirectoryMetadataWithoutTreatingItAsAppOverrides()
    {
        WithDirectory(root =>
        {
            var path = Path.Combine(root, ".env");
            File.WriteAllText(path, string.Join('\n', new[] { "DAVIS", "JKBMS", "SMARTSHUNT", "EG4" }
                .SelectMany(prefix => new[]
                {
                    $"{prefix}_SECRETS_DIRECTORY=./secrets",
                    $"{prefix}_REMOTE_SECRETS_DIRECTORY=/fixture/secrets"
                })));

            EdgeRuntimeEnvironmentFile.Read(path, root).Should().BeEmpty();
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
        File.WriteAllText(json, """{"Edge":{"Runtime":{"ServiceName":"fixture-default"}},"Outbox":{"BatchSize":50},"HvoFixturePrecedence":{"Value":"mounted-json"}}""");
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

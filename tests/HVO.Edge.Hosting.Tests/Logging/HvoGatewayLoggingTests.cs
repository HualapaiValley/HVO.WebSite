using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Hosting.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.OpenTelemetry;

namespace HVO.Edge.Hosting.Tests.Logging;

[TestClass]
public sealed class HvoGatewayLoggingTests
{
    [TestMethod]
    public void Configure_AddsStableMetadataAndRedactsSensitiveProperties()
    {
        var sink = new CollectingSink();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["OTEL_SERVICE_NAME"] = "configured-service",
            ["HOSTNAME"] = "gateway-host"
        });
        var contentRoot = Directory.CreateTempSubdirectory("hvo-logging-test-").FullName;
        var loggerConfiguration = HvoGatewayLoggingBuilderExtensions.Configure(
                new LoggerConfiguration(),
                configuration,
                new TestHostEnvironment { ContentRootPath = contentRoot },
                new GatewayLogIdentity("default-service", "gateway-01", "battery", "source-01", "device-01"),
                writeToConsole: false)
            .WriteTo.Sink(sink);

        using var activity = new Activity("logging-test").Start();
        using var logger = loggerConfiguration.CreateLogger();
        logger.Information("Forwarding with {ApiKey} and {AccessToken}", "secret-key", "secret-token");

        var logEvent = sink.Events.Should().ContainSingle().Subject;
        logEvent.Properties["service.name"].LiteralValue().Should().Be("configured-service");
        logEvent.Properties["deployment.environment.name"].LiteralValue().Should().Be(Environments.Production);
        logEvent.Properties["host.name"].LiteralValue().Should().Be("gateway-host");
        logEvent.Properties["hvo.gateway.id"].LiteralValue().Should().Be("gateway-01");
        logEvent.Properties["hvo.gateway.type"].LiteralValue().Should().Be("battery");
        logEvent.Properties["hvo.source.id"].LiteralValue().Should().Be("source-01");
        logEvent.Properties["hvo.device.id"].LiteralValue().Should().Be("device-01");
        logEvent.Properties["ApiKey"].LiteralValue().Should().Be("[REDACTED]");
        logEvent.Properties["AccessToken"].LiteralValue().Should().Be("[REDACTED]");
        logEvent.Properties.Should().ContainKey("TraceId");
        logEvent.Properties.Should().ContainKey("SpanId");
        logEvent.Properties.Should().ContainKey("ParentId");
        logEvent.Properties.Should().ContainKey("CorrelationId");
        Directory.Exists(Path.Combine(contentRoot, "logs")).Should().BeFalse();

        Directory.Delete(contentRoot, recursive: true);
    }

    [TestMethod]
    public void Configure_HonorsDefaultAndCategoryLogLevels()
    {
        var sink = new CollectingSink();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:HVO.TestCategory"] = "Debug"
        });
        using var logger = HvoGatewayLoggingBuilderExtensions.Configure(
                new LoggerConfiguration(),
                configuration,
                new TestHostEnvironment(),
                new GatewayLogIdentity("service", "gateway", "test"),
                writeToConsole: false)
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Debug("Filtered default debug event");
        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "HVO.TestCategory")
            .Debug("Accepted category debug event");

        sink.Events.Should().ContainSingle()
            .Which.MessageTemplate.Text.Should().Be("Accepted category debug event");
    }

    [TestMethod]
    public void Configure_HonorsNoneForCategoryAndDefault()
    {
        var sink = new CollectingSink();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "None",
            ["Logging:LogLevel:HVO.Enabled"] = "Information",
            ["Logging:LogLevel:HVO.Enabled.Noisy"] = "None"
        });
        using var logger = HvoGatewayLoggingBuilderExtensions.Configure(
                new LoggerConfiguration(),
                configuration,
                new TestHostEnvironment(),
                new GatewayLogIdentity("service", "gateway", "test"),
                writeToConsole: false)
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Filtered default event");
        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "HVO.Enabled")
            .Information("Accepted category event");
        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "HVO.Enabled.Noisy")
            .Fatal("Filtered disabled category event");

        sink.Events.Should().ContainSingle()
            .Which.MessageTemplate.Text.Should().Be("Accepted category event");
    }

    [TestMethod]
    public void BuildResourceAttributes_IncludesOptionalIdentityMetadata()
    {
        var attributes = HvoGatewayLoggingBuilderExtensions.BuildResourceAttributes(
            new GatewayLogIdentity("service", "gateway", "battery", "source", "device"),
            "service",
            "1.2.3",
            "instance",
            "Production",
            "host");

        attributes.Should().Contain(new Dictionary<string, object>
        {
            ["service.name"] = "service",
            ["service.version"] = "1.2.3",
            ["service.instance.id"] = "instance",
            ["deployment.environment.name"] = "Production",
            ["host.name"] = "host",
            ["hvo.gateway.id"] = "gateway",
            ["hvo.gateway.type"] = "battery",
            ["hvo.source.id"] = "source",
            ["hvo.device.id"] = "device"
        });
    }

    [TestMethod]
    public void ConfigureOtlpOptions_RegistersOnlyResolvedLogsEndpoint()
    {
        var options = new BatchedOpenTelemetrySinkOptions();
        var attributes = new Dictionary<string, object> { ["service.name"] = "service" };

        HvoGatewayLoggingBuilderExtensions.ConfigureOtlpOptions(
            options,
            new OtlpLogExportSettings(
                "https://logs.example.test/custom/v1/logs",
                OtlpProtocol.HttpProtobuf,
                new Dictionary<string, string> { ["Authorization"] = "Bearer test" }),
            attributes);

        options.Endpoint.Should().BeNull();
        options.LogsEndpoint.Should().Be("https://logs.example.test/custom/v1/logs");
        options.TracesEndpoint.Should().BeNull();
        options.Protocol.Should().Be(OtlpProtocol.HttpProtobuf);
        options.ResourceAttributes.Should().BeSameAs(attributes);
        options.Headers["Authorization"].Should().Be("Bearer test");
        options.BatchingOptions.BatchSizeLimit.Should().Be(256);
        options.BatchingOptions.BufferingTimeLimit.Should().Be(TimeSpan.FromSeconds(2));
        options.BatchingOptions.QueueLimit.Should().Be(5_000);
        options.BatchingOptions.RetryTimeLimit.Should().Be(TimeSpan.FromMinutes(10));
    }

    [TestMethod]
    public void ConsoleFormatter_AlwaysWritesTimestampAndSeverity()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.Parse("2026-08-09T12:00:00Z"),
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse("Gateway started"),
            []);
        using var output = new StringWriter();

        HvoGatewayLoggingBuilderExtensions.CreateConsoleFormatter().Format(logEvent, output);

        using var json = JsonDocument.Parse(output.ToString());
        json.RootElement.GetProperty("Timestamp").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("Level").GetString().Should().Be("Information");
    }

    [TestMethod]
    public void SanitizingSink_RedactsNestedValuesAndExceptionText()
    {
        var nested = new StructureValue(
        [
            new LogEventProperty("X-Api-Key", new ScalarValue("nested-secret")),
            new LogEventProperty("Endpoint", new ScalarValue("https://user:password@example.test/path?token=query-secret"))
        ]);
        var original = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Warning,
            new InvalidOperationException("Request failed with Authorization: Bearer exception-secret at https://user:password@example.test/path?token=query-secret"),
            new MessageTemplateParser().Parse("Failure {Context}; api-key=literal-secret"),
            [new LogEventProperty("Context", nested)]);

        var sanitized = SanitizingLogEventSink.Sanitize(original);

        sanitized.Exception.Should().BeNull();
        sanitized.Properties["exception.type"].LiteralValue().Should().Be(typeof(InvalidOperationException).FullName);
        var exceptionMessage = sanitized.Properties["exception.message"].LiteralValue()
            .Should().BeOfType<string>().Subject;
        exceptionMessage.Should().NotContain("exception-secret");
        exceptionMessage.Should().NotContain("password");
        exceptionMessage.Should().NotContain("query-secret");
        sanitized.MessageTemplate.Text.Should().Be("Failure {Context}; api-key=[REDACTED]");
        var context = sanitized.Properties["Context"].Should().BeOfType<StructureValue>().Subject;
        context.Properties.Single(property => property.Name == "X-Api-Key").Value.LiteralValue().Should().Be("[REDACTED]");
        context.Properties.Single(property => property.Name == "Endpoint").Value.LiteralValue()
            .Should().Be("https://example.test/path");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "HVO.Edge.Hosting.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal static class LogEventPropertyValueAssertionsExtensions
{
    public static object? LiteralValue(this LogEventPropertyValue value) =>
        value.Should().BeOfType<ScalarValue>().Subject.Value;
}

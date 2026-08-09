using System.Reflection;
using HVO.Enterprise.Telemetry.Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using Serilog.Sinks.OpenTelemetry;

namespace HVO.Edge.Hosting.Logging;

public static class HvoGatewayLoggingBuilderExtensions
{
    private static readonly TimeSpan RepeatedFailureInterval = TimeSpan.FromMinutes(1);

    public static IHostBuilder UseHvoGatewayLogging(
        this IHostBuilder hostBuilder,
        GatewayLogIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        ValidateIdentity(identity);

        return hostBuilder.UseSerilog((context, _, loggerConfiguration) =>
            Configure(loggerConfiguration, context.Configuration, context.HostingEnvironment, identity));
    }

    internal static LoggerConfiguration Configure(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        IHostEnvironment environment,
        GatewayLogIdentity identity,
        bool writeToConsole = true)
    {
        ValidateIdentity(identity);

        var serviceName = configuration["OTEL_SERVICE_NAME"]
            ?? configuration["Telemetry:ServiceName"]
            ?? identity.DefaultServiceName;
        var serviceVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var hostName = configuration["HOSTNAME"] ?? Environment.MachineName;
        var instanceId = configuration["OTEL_SERVICE_INSTANCE_ID"] ?? hostName;
        var configuredLevels = configuration.GetSection("Logging:LogLevel").GetChildren().ToList();
        var defaultDisabled = IsNone(configuration["Logging:LogLevel:Default"]);
        var categoryDisabled = configuredLevels
            .Where(level => !level.Key.Equals("Default", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(level => level.Key, level => IsNone(level.Value), StringComparer.Ordinal);

        loggerConfiguration
            .MinimumLevel.Is(GetLevel(configuration["Logging:LogLevel:Default"], LogEventLevel.Information))
            .MinimumLevel.Override("Microsoft", GetLevel(configuration["Logging:LogLevel:Microsoft"], LogEventLevel.Warning))
            .MinimumLevel.Override(
                "Microsoft.EntityFrameworkCore",
                GetLevel(configuration["Logging:LogLevel:Microsoft.EntityFrameworkCore"], LogEventLevel.Warning));

        foreach (var levelOverride in configuredLevels)
        {
            if (!levelOverride.Key.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                TryGetLevel(levelOverride.Value, out var level))
            {
                loggerConfiguration.MinimumLevel.Override(levelOverride.Key, level);
            }
        }

        loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithTelemetry()
            .Enrich.With(new SensitivePropertyRedactionEnricher())
            .Enrich.WithProperty("service.name", serviceName)
            .Enrich.WithProperty("service.version", serviceVersion)
            .Enrich.WithProperty("service.instance.id", instanceId)
            .Enrich.WithProperty("deployment.environment.name", environment.EnvironmentName)
            .Enrich.WithProperty("host.name", hostName)
            .Enrich.WithProperty("hvo.gateway.id", identity.GatewayId)
            .Enrich.WithProperty("hvo.gateway.type", identity.GatewayType)
            .Filter.With(new NoneLevelFilter(defaultDisabled, categoryDisabled))
            .Filter.With(new RepeatedFailureFilter(RepeatedFailureInterval));

        if (!string.IsNullOrWhiteSpace(identity.SourceId))
        {
            loggerConfiguration.Enrich.WithProperty("hvo.source.id", identity.SourceId);
        }

        if (!string.IsNullOrWhiteSpace(identity.DeviceId))
        {
            loggerConfiguration.Enrich.WithProperty("hvo.device.id", identity.DeviceId);
        }

        if (writeToConsole)
        {
            var consoleLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(CreateConsoleFormatter())
                .CreateLogger();
            loggerConfiguration.WriteTo.Sink(new SanitizingLogEventSink(consoleLogger));
        }

        var otlp = OtlpLogEndpointResolver.Resolve(configuration, environment);
        if (otlp is not null)
        {
            var otlpLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.OpenTelemetry(options => ConfigureOtlpOptions(
                    options,
                    otlp,
                    BuildResourceAttributes(
                        identity,
                        serviceName,
                        serviceVersion,
                        instanceId,
                        environment.EnvironmentName,
                        hostName)),
                    ignoreEnvironment: true)
                .CreateLogger();
            loggerConfiguration.WriteTo.Sink(new SanitizingLogEventSink(otlpLogger));
        }

        return loggerConfiguration;
    }

    internal static ITextFormatter CreateConsoleFormatter() => new JsonFormatter(renderMessage: true);

    internal static void ConfigureOtlpOptions(
        BatchedOpenTelemetrySinkOptions options,
        OtlpLogExportSettings settings,
        Dictionary<string, object> resourceAttributes)
    {
        options.Protocol = settings.Protocol;
        options.Endpoint = null;
        options.LogsEndpoint = settings.Endpoint;
        options.TracesEndpoint = null;
        options.ResourceAttributes = resourceAttributes;
        options.BatchingOptions.BatchSizeLimit = 256;
        options.BatchingOptions.BufferingTimeLimit = TimeSpan.FromSeconds(2);
        options.BatchingOptions.QueueLimit = 5_000;
        options.BatchingOptions.RetryTimeLimit = TimeSpan.FromMinutes(10);
        foreach (var header in settings.Headers ?? new Dictionary<string, string>())
        {
            options.Headers[header.Key] = header.Value;
        }
    }

    internal static Dictionary<string, object> BuildResourceAttributes(
        GatewayLogIdentity identity,
        string serviceName,
        string serviceVersion,
        string instanceId,
        string environmentName,
        string hostName)
    {
        var attributes = new Dictionary<string, object>
        {
            ["service.name"] = serviceName,
            ["service.version"] = serviceVersion,
            ["service.instance.id"] = instanceId,
            ["deployment.environment.name"] = environmentName,
            ["host.name"] = hostName,
            ["hvo.gateway.id"] = identity.GatewayId,
            ["hvo.gateway.type"] = identity.GatewayType
        };

        if (!string.IsNullOrWhiteSpace(identity.SourceId))
        {
            attributes["hvo.source.id"] = identity.SourceId;
        }

        if (!string.IsNullOrWhiteSpace(identity.DeviceId))
        {
            attributes["hvo.device.id"] = identity.DeviceId;
        }

        return attributes;
    }

    private static void ValidateIdentity(GatewayLogIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.DefaultServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.GatewayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.GatewayType);
    }

    private static LogEventLevel GetLevel(string? configuredLevel, LogEventLevel fallback) =>
        TryGetLevel(configuredLevel, out var level) ? level : fallback;

    private static bool TryGetLevel(string? configuredLevel, out LogEventLevel level)
    {
        level = configuredLevel?.Trim().ToLowerInvariant() switch
        {
            "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "critical" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        return configuredLevel?.Trim().ToLowerInvariant() is
            "trace" or "debug" or "information" or "warning" or "error" or "critical";
    }

    private static bool IsNone(string? configuredLevel) =>
        string.Equals(configuredLevel?.Trim(), "None", StringComparison.OrdinalIgnoreCase);
}

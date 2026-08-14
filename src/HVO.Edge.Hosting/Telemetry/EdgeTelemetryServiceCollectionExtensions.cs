using HVO.Edge.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HVO.Edge.Hosting.Telemetry;

public static class EdgeTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddHvoEdgeTelemetry(
        this IServiceCollection services,
        EdgeRuntimeIdentity identity,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(identity.ToTelemetryIdentity());
        services.AddSingleton<GatewayTelemetry>();

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddAttributes(identity.CreateResourceAttributes()))
            .WithTracing(tracing => tracing
                .AddSource(GatewayTelemetryConventions.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(GatewayTelemetryConventions.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        var traceExport = OtlpSignalEndpointResolver.Resolve(configuration, "traces");
        if (traceExport is not null)
        {
            telemetry.WithTracing(tracing => tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = traceExport.Endpoint;
                options.Protocol = traceExport.Protocol;
            }));
        }

        var metricExport = OtlpSignalEndpointResolver.Resolve(configuration, "metrics");
        if (metricExport is not null)
        {
            telemetry.WithMetrics(metrics => metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = metricExport.Endpoint;
                options.Protocol = metricExport.Protocol;
            }));
        }

        return services;
    }

    public static bool HasConfiguredOtlpEndpoint(IConfiguration configuration) =>
        TryEndpoint(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"], out _)
        || TryEndpoint(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"], out _)
        || TryEndpoint(configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"], out _)
        || TryEndpoint(configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"], out _);

    private static bool TryEndpoint(string? value, out Uri endpoint) =>
        Uri.TryCreate(value, UriKind.Absolute, out endpoint!);
}

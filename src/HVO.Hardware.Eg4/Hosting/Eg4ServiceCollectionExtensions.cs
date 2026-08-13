using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Diagnostics;
using HVO.Hardware.Eg4.HomeAssistant;
using HVO.Hardware.Eg4.Outbox;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Simulation;
using HVO.Hardware.Eg4.Telemetry;
using HVO.Hardware.Eg4.Workers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Hosting;

public static class Eg4ServiceCollectionExtensions
{
    public static IServiceCollection AddEg4Collector(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<Eg4Options>()
            .Bind(configuration.GetSection(Eg4Options.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Eg4Options>>(_ => new Eg4OptionsValidator(environment));
        services.TryAddSingleton(TimeProvider.System);

        if (configuration.GetValue<bool>($"{Eg4Options.SectionName}:SimulationEnabled"))
        {
            services.AddSingleton<Eg4FleetSimulator>();
            services.AddSingleton<IEg4TelemetrySource>(provider => provider.GetRequiredService<Eg4FleetSimulator>());
            services.AddSingleton<ScriptedEg4RegisterTransportFactory>();
            services.AddSingleton<IEg4RegisterTransportFactory>(provider =>
                provider.GetRequiredService<ScriptedEg4RegisterTransportFactory>());
            services.AddSingleton<IEg4PortCoordinator, Eg4PortCoordinator>();
        }
        else
        {
            services.AddSingleton<Eg46500ExHidrawTransportFactory>();
            services.AddSingleton<IEg46500ExInquiryTransportFactory>(provider =>
                provider.GetRequiredService<Eg46500ExHidrawTransportFactory>());
            services.AddSingleton<Eg46500ExTelemetrySource>();
            services.AddSingleton<IEg4DeviceTelemetrySource>(provider => provider.GetRequiredService<Eg46500ExTelemetrySource>());
            services.AddSingleton<Eg4Mppt10048HvSerialTransportFactory>();
            services.AddSingleton<IEg4RegisterTransportFactory>(provider =>
                provider.GetRequiredService<Eg4Mppt10048HvSerialTransportFactory>());
            services.AddSingleton<IEg4PortCoordinator, Eg4PortCoordinator>();
            services.AddSingleton<Eg4Mppt10048HvTelemetrySource>();
            services.AddSingleton<IEg4DeviceTelemetrySource>(provider => provider.GetRequiredService<Eg4Mppt10048HvTelemetrySource>());
            services.AddSingleton<IEg4TelemetrySource, Eg4TelemetrySourceRouter>();
        }

        services.AddSingleton<Eg4CentralIngestCredential>();
        services.AddHostedService<Eg4CollectorInitializer>();
        services.AddScoped<IEg4PowerOutboxWriter, PowerOutboxWriter>();
        services.AddSingleton<IEdgeOutboxBatchSender, Eg4OutboxBatchSender>();
        services.AddHostedService<Eg4RetryRequeueWorker>();
        services.AddSingleton<Eg4RuntimeState>();
        services.AddSingleton<Eg4HomeAssistantProjection>();
        services.RemoveAll<IEdgeDiagnosticsSnapshotProvider>();
        services.AddSingleton<IEdgeDiagnosticsSnapshotProvider, Eg4DiagnosticsSnapshotProvider>();
        services.AddHostedService<Eg4FleetWorker>();
        return services;
    }
}

internal sealed class Eg4CollectorInitializer(
    IOptions<Eg4Options> options,
    IOptions<EdgeOutboxOptions> outboxOptions,
    SecretFileResolver secretResolver,
    Eg4CentralIngestCredential credential) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (outboxOptions.Value.PayloadType != HVO.Edge.Contracts.EdgePayloadTypes.Eg4Observation
            || outboxOptions.Value.PayloadVersion != "1")
            throw new InvalidOperationException("EG4 requires Outbox:PayloadType com.hvo.eg4.observation.v1 and PayloadVersion 1.");
        credential.ApiKey = secretResolver.ReadRequired(options.Value.CentralApiKeySecret, "Eg4:CentralApiKeySecret");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal static class HomeAssistantExporterRegistration
{
    public static IServiceCollection AddHomeAssistantExporter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HomeAssistantExporterOptions>()
            .Bind(configuration.GetSection(HomeAssistantExporterOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HomeAssistantExporterOptions>, HomeAssistantExporterOptionsValidator>());
        services.AddSingleton<HomeAssistantExporterCredential>();
        services.AddHostedService<HomeAssistantExporterInitializer>();
        services.AddSingleton<HomeAssistantExporterState>();
        services.AddSingleton<HomeAssistantStateProjector>();
        services.AddSingleton<IHomeAssistantEventSource, HomeAssistantWebSocketClient>();
        services.AddSingleton<IHomeAssistantObservationWriter, HomeAssistantObservationWriter>();
        services.AddSingleton<IEdgeOutboxBatchSender, HomeAssistantOutboxBatchSender>();
        services.AddHostedService<HomeAssistantRetryRequeueWorker>();
        services.RemoveAll<IEdgeDiagnosticsSnapshotProvider>();
        services.AddSingleton<IEdgeDiagnosticsSnapshotProvider, HomeAssistantExporterDiagnosticsProvider>();
        services.AddHostedService<HomeAssistantExporterWorker>();
        return services;
    }
}

internal sealed class HomeAssistantExporterInitializer(
    IOptions<HomeAssistantExporterOptions> options,
    SecretFileResolver secretResolver,
    HomeAssistantExporterCredential credential) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        credential.CentralApiKey = secretResolver.ReadRequired(
            options.Value.CentralApiKeySecret, "HomeAssistant:Exporter:CentralApiKeySecret");
        if (options.Value.Enabled)
        {
            credential.AccessToken = secretResolver.ReadRequired(
                options.Value.AccessTokenSecret, "HomeAssistant:Exporter:AccessTokenSecret");
        }
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

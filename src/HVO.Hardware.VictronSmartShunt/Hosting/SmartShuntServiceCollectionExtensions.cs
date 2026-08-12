using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Diagnostics;
using HVO.Hardware.VictronSmartShunt.HomeAssistant;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Hosting;

public static class SmartShuntServiceCollectionExtensions
{
    public static IServiceCollection AddSmartShuntCollector(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmartShuntOptions>().Bind(configuration.GetSection(SmartShuntOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IValidateOptions<SmartShuntOptions>, SmartShuntOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SmartShuntCentralIngestCredential>();
        services.AddHostedService<SmartShuntCollectorInitializer>();
        services.AddScoped<ISmartShuntOutboxWriter, SmartShuntOutboxWriter>();
        services.AddSingleton<IEdgeOutboxBatchSender, SmartShuntOutboxBatchSender>();
        services.AddHostedService<SmartShuntRetryRequeueWorker>();
        services.AddSingleton<SmartShuntHomeAssistantProjection>();
        services.AddSingleton<ISmartShuntHomeAssistantProjection>(provider => provider.GetRequiredService<SmartShuntHomeAssistantProjection>());
        services.AddSingleton<SmartShuntPublicSession>();
        services.AddSingleton<ISmartShuntSessionState>(provider => provider.GetRequiredService<SmartShuntPublicSession>());
        services.AddHostedService(provider => provider.GetRequiredService<SmartShuntPublicSession>());
        services.AddSingleton<SmartShuntWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<SmartShuntWorker>());
        services.RemoveAll<IEdgeDiagnosticsSnapshotProvider>();
        services.AddSingleton<IEdgeDiagnosticsSnapshotProvider, SmartShuntDiagnosticsSnapshotProvider>();
        return services;
    }

    public static async Task MigrateSmartShuntLegacyOutboxAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<EdgeOutboxOptions>>().Value;
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath) ?? throw new InvalidOperationException("Outbox database needs a parent directory."));
        await SmartShuntLegacyOutboxMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>(), cancellationToken);
    }
}

internal sealed class SmartShuntCollectorInitializer(IOptions<SmartShuntOptions> options, IOptions<EdgeOutboxOptions> outbox, SecretFileResolver secrets, SmartShuntCentralIngestCredential credential) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (outbox.Value.PayloadType != EdgePayloadTypes.SmartShuntObservation || outbox.Value.PayloadVersion != "1")
            throw new InvalidOperationException("SmartShunt requires com.hvo.smartshunt.observation.v1 payloads.");
        credential.ApiKey = secrets.ReadRequired(options.Value.CentralApiKeySecret, "SmartShunt:CentralApiKeySecret");
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

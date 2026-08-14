using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Cwop;
using HVO.Hardware.DavisVantagePro2.Diagnostics;
using HVO.Hardware.DavisVantagePro2.HomeAssistant;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Workers;
using HVO.Hardware.DavisVantagePro2.WeatherUnderground;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

namespace HVO.Hardware.DavisVantagePro2.Hosting;

public static class DavisServiceCollectionExtensions
{
    public static IServiceCollection AddDavisCollector(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StationOptions>()
            .Bind(configuration.GetSection(StationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StationOptions>, StationOptionsValidator>();
        services.AddOptions<WeatherUndergroundOptions>()
            .Bind(configuration.GetSection(WeatherUndergroundOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WeatherUndergroundOptions>, WeatherUndergroundOptionsValidator>();
        services.AddOptions<CwopOptions>()
            .Bind(configuration.GetSection(CwopOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CwopOptions>, CwopOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<DavisLocalDbContext>((provider, builder) =>
            builder.UseSqlite($"Data Source={provider.GetRequiredService<IOptions<StationOptions>>().Value.LocalDatabasePath}"));
        services.AddSingleton(provider =>
        {
            var value = provider.GetRequiredService<IOptions<StationOptions>>().Value;
            return new DavisConsoleClient(
                value.Host,
                value.Port,
                TimeSpan.FromSeconds(value.SocketTimeoutSeconds),
                provider.GetRequiredService<ILogger<DavisConsoleClient>>());
        });
        services.AddSingleton<VantageStation>();
        services.AddSingleton<IDavisStation>(provider => provider.GetRequiredService<VantageStation>());
        services.AddSingleton<StationSettingsSnapshotStore>();
        services.AddSingleton<IStationSettingsSnapshotStore>(provider => provider.GetRequiredService<StationSettingsSnapshotStore>());
        services.AddSingleton<StationInfoSnapshotStore>();
        services.AddSingleton<IStationInfoSnapshotStore>(provider => provider.GetRequiredService<StationInfoSnapshotStore>());
        services.AddSingleton<DavisArchiveCursorStore>();
        services.AddSingleton<IDavisArchiveCursorStore>(provider => provider.GetRequiredService<DavisArchiveCursorStore>());
        services.AddScoped<DavisOutboxWriter>();
        services.AddScoped<IDavisOutboxWriter>(provider => provider.GetRequiredService<DavisOutboxWriter>());
        services.AddSingleton<DavisCentralIngestCredential>();
        services.AddHostedService<DavisCollectorInitializer>();
        services.AddSingleton<IEdgeOutboxBatchSender, DavisOutboxBatchSender>();
        services.AddHostedService<DavisRetryRequeueWorker>();
        services.AddSingleton<DavisRuntimeState>();
        services.AddSingleton<WeatherUndergroundCredential>();
        services.AddSingleton<WeatherUndergroundPublisherState>();
        services.AddSingleton<WeatherUndergroundMetrics>();
        services.AddHostedService<WeatherUndergroundInitializer>();
        services.AddHttpClient<WeatherUndergroundClient>(client =>
            {
                client.BaseAddress = WeatherUndergroundQueryBuilder.Endpoint;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .RemoveAllLoggers();
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(WeatherUndergroundMetrics.MeterName));
        services.AddSingleton<WeatherUndergroundPublisher>();
        services.AddHostedService(provider => provider.GetRequiredService<WeatherUndergroundPublisher>());
        services.AddSingleton<CwopCredential>();
        services.AddSingleton<CwopPublisherState>();
        services.AddSingleton<CwopClient>();
        services.AddSingleton<ICwopClient>(provider => provider.GetRequiredService<CwopClient>());
        services.AddHostedService<CwopInitializer>();
        services.AddSingleton<CwopPublisher>();
        services.AddHostedService(provider => provider.GetRequiredService<CwopPublisher>());
        services.AddSingleton<DavisHomeAssistantProjection>();
        services.AddSingleton<IDavisHomeAssistantProjection>(provider => provider.GetRequiredService<DavisHomeAssistantProjection>());
        services.RemoveAll<IEdgeDiagnosticsSnapshotProvider>();
        services.AddSingleton<IEdgeDiagnosticsSnapshotProvider, DavisDiagnosticsSnapshotProvider>();
        services.AddSingleton<WeatherStationWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<WeatherStationWorker>());
        return services;
    }

    public static async Task MigrateDavisLocalStateAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var stationOptions = scope.ServiceProvider.GetRequiredService<IOptions<StationOptions>>().Value;
        Directory.CreateDirectory(Path.GetDirectoryName(stationOptions.LocalDatabasePath)
            ?? throw new InvalidOperationException("Station:LocalDatabasePath must have a parent directory."));
        await DavisLocalDatabaseInitializer.EnsureCreatedAsync(
            scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>(), cancellationToken);
        var cached = await scope.ServiceProvider.GetRequiredService<StationSettingsSnapshotStore>().GetAsync(cancellationToken);
        if (cached is not null)
            scope.ServiceProvider.GetRequiredService<IDavisStation>().ApplyStationSettings(cached.Settings);

        var outboxOptions = scope.ServiceProvider.GetRequiredService<IOptions<EdgeOutboxOptions>>().Value;
        Directory.CreateDirectory(Path.GetDirectoryName(outboxOptions.DatabasePath)
            ?? throw new InvalidOperationException("Outbox:DatabasePath must have a parent directory."));
        await DavisLegacyOutboxMigrator.MigrateAsync(
            scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>(),
            stationOptions.StationId,
            TimeSpan.FromHours(stationOptions.LegacyArchiveConsoleUtcOffsetHours!.Value),
            cancellationToken);
    }
}

internal sealed class CwopInitializer(
    IOptions<CwopOptions> options,
    SecretFileResolver secretResolver,
    CwopCredential credential) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            var passcode = string.IsNullOrWhiteSpace(options.Value.PasscodeSecret)
                ? options.Value.Passcode
                : secretResolver.ReadRequired(options.Value.PasscodeSecret, "Cwop:PasscodeSecret");
            credential.Initialize(passcode);
        }
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DavisCollectorInitializer(
    IOptions<StationOptions> stationOptions,
    IOptions<EdgeOutboxOptions> outboxOptions,
    SecretFileResolver secretResolver,
    DavisCentralIngestCredential credential) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var payloadTypes = outboxOptions.Value.EffectivePayloadTypes.ToHashSet(StringComparer.Ordinal);
        if (!payloadTypes.SetEquals([EdgePayloadTypes.WeatherRaw, EdgePayloadTypes.WeatherArchive])
            || outboxOptions.Value.PayloadVersion != "1")
            throw new InvalidOperationException("Davis requires live and archive v1 outbox payload types.");
        credential.ApiKey = secretResolver.ReadRequired(stationOptions.Value.CentralApiKeySecret, "Station:CentralApiKeySecret");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

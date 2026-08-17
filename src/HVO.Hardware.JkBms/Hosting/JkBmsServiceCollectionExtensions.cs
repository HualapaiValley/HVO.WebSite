using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Configuration;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Diagnostics;
using HVO.Hardware.JkBms.HomeAssistant;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Hosting;

public static class JkBmsServiceCollectionExtensions
{
    public static IServiceCollection AddJkBmsCollector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JkBmsOptions>()
            .Bind(configuration.GetSection(JkBmsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JkBmsOptions>, JkBmsOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IBluetoothAdapterCoordinator, BluetoothAdapterCoordinator>();
        services.AddSingleton<IBmsTransportFactory, JkBmsBluetoothTransportFactory>();
        services.AddSingleton<JkBmsCentralIngestCredential>();
        services.AddSingleton<JkBmsSettingsPasswordCredentials>();
        services.AddHostedService<JkBmsCollectorInitializer>();
        services.AddScoped<IBmsOutboxWriter, BmsOutboxWriter>();
        services.AddSingleton<IEdgeOutboxBatchSender, JkBmsOutboxBatchSender>();
        services.AddHostedService<JkBmsRetryRequeueWorker>();
        services.AddSingleton<JkBmsHomeAssistantProjection>();
        services.RemoveAll<IEdgeDiagnosticsSnapshotProvider>();
        services.AddSingleton<IEdgeDiagnosticsSnapshotProvider, JkBmsDiagnosticsSnapshotProvider>();
        services.AddSingleton<BmsPollerWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<BmsPollerWorker>());
        return services;
    }

    public static async Task MigrateJkBmsLegacyOutboxAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<EdgeOutboxOptions>>().Value;
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)
            ?? throw new InvalidOperationException("Outbox:DatabasePath must have a parent directory."));
        var result = await JkBmsLegacyOutboxMigrator.MigrateAsync(
            scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>(),
            cancellationToken);
        if (result.SchemaMigrated || result.CanonicalizedReadingCount > 0 || result.AccountedSnapshotCount > 0)
        {
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("JkBmsLegacyOutboxMigration")
                .LogInformation(
                    "JK BMS legacy outbox migration completed. SchemaMigrated={SchemaMigrated} CanonicalizedReadings={CanonicalizedReadings} AccountedSnapshots={AccountedSnapshots}",
                    result.SchemaMigrated,
                    result.CanonicalizedReadingCount,
                    result.AccountedSnapshotCount);
        }
    }
}

internal sealed class JkBmsCollectorInitializer(
    IOptions<JkBmsOptions> options,
    IOptions<EdgeOutboxOptions> outboxOptions,
    SecretFileResolver secretResolver,
    JkBmsCentralIngestCredential credential,
    JkBmsSettingsPasswordCredentials settingsPasswordCredentials) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (outboxOptions.Value.PayloadType != EdgePayloadTypes.BmsReading
            || outboxOptions.Value.PayloadVersion != "1")
            throw new InvalidOperationException("JK BMS requires Outbox:PayloadType com.hvo.bms.reading.v1 and PayloadVersion 1.");
        credential.ApiKey = secretResolver.ReadRequired(options.Value.CentralApiKeySecret, "JkBms:CentralApiKeySecret");
        foreach (var device in options.Value.Devices.Where(static device =>
                     device.Enabled && !string.IsNullOrWhiteSpace(device.SettingsPasswordSecret)))
        {
            var password = secretResolver.ReadRequired(
                device.SettingsPasswordSecret!,
                $"JkBms:Devices[{device.DeviceId}]:SettingsPasswordSecret");
            if (password.Length != 6 || password.AsSpan().IndexOfAnyExceptInRange('0', '9') >= 0)
                throw new InvalidOperationException(
                    $"The settings-password secret for JK BMS device '{device.DeviceId}' must contain exactly six ASCII digits.");
            settingsPasswordCredentials.Set(device.DeviceId, password);
        }
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class JkBmsSettingsPasswordCredentials
{
    private readonly Dictionary<string, string> passwords = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string deviceId, string password) => passwords[deviceId] = password;

    public string? Get(string deviceId) => passwords.GetValueOrDefault(deviceId);
}

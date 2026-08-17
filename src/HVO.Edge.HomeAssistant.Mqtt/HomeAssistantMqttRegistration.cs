using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt;

public static class HomeAssistantMqttRegistration
{
    public static IServiceCollection AddHvoHomeAssistantMqtt(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddLogging();
        services.AddOptions<HomeAssistantMqttOptions>()
            .Bind(configuration.GetSection(HomeAssistantMqttOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HomeAssistantMqttOptions>, HomeAssistantMqttOptionsValidator>());
        services.AddSingleton<HomeAssistantMqttProjection>();
        services.AddSingleton<IHomeAssistantMqttProjection>(provider => provider.GetRequiredService<HomeAssistantMqttProjection>());
        services.AddSingleton<HomeAssistantMqttCommandRouter>();
        services.AddSingleton<IHomeAssistantMqttCommandRouter>(provider => provider.GetRequiredService<HomeAssistantMqttCommandRouter>());
        services.TryAddSingleton<IMqttSession, MqttNetSession>();
        services.AddSingleton<MqttRuntimeCredential>();
        services.AddHostedService<HomeAssistantMqttInitializer>();
        services.AddHostedService<HomeAssistantMqttWorker>();
        return services;
    }

    public static IServiceCollection AddHvoHomeAssistantGatewayDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<HomeAssistantGatewayDiagnosticsWorker>();
        return services;
    }
}

internal sealed class MqttRuntimeCredential
{
    public MqttConnectionSettings? Settings { get; set; }
}

internal sealed class HomeAssistantMqttInitializer(
    IOptions<HomeAssistantMqttOptions> options,
    EdgeRuntimeIdentity identity,
    SecretFileResolver secretResolver,
    MqttRuntimeCredential credential) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var value = options.Value;
        if (!value.Enabled)
            return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(identity.SiteId))
            throw new InvalidOperationException("Edge:Runtime:SiteId is required when Home Assistant MQTT is enabled.");

        var topics = new HomeAssistantMqttTopics(value.DiscoveryPrefix, value.TopicPrefix);
        var key = new HomeAssistantDeviceKey(identity.SiteId, identity.GatewayId, identity.DeviceId ?? identity.GatewayId);
        credential.Settings = new(
            value.Host!,
            value.Port,
            HomeAssistantMqttIdentity.GatewayClientId(identity.SiteId, identity.GatewayId),
            secretResolver.ReadRequired(value.UsernameSecret!, "HomeAssistant:Mqtt:UsernameSecret"),
            secretResolver.ReadRequired(value.PasswordSecret!, "HomeAssistant:Mqtt:PasswordSecret"),
            topics.GatewayAvailability(key));
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

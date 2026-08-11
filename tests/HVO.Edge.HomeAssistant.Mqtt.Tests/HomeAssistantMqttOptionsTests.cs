using FluentAssertions;
using HVO.Edge.Hosting.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

[TestClass]
public sealed class HomeAssistantMqttOptionsTests
{
    [TestMethod]
    public void EnabledOptions_RejectMissingHostInvalidPrefixesAndReconnectBounds()
    {
        using var provider = CreateOptionsProvider(new Dictionary<string, string?>
        {
            ["HomeAssistant:Mqtt:Enabled"] = "true",
            ["HomeAssistant:Mqtt:UsernameSecret"] = "user",
            ["HomeAssistant:Mqtt:PasswordSecret"] = "password",
            ["HomeAssistant:Mqtt:DiscoveryPrefix"] = "homeassistant/#",
            ["HomeAssistant:Mqtt:InitialReconnectDelaySeconds"] = "30",
            ["HomeAssistant:Mqtt:MaxReconnectDelaySeconds"] = "10"
        });

        var act = () => provider.GetRequiredService<IOptions<HomeAssistantMqttOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Where(exception => exception.Failures.Any(failure => failure.Contains("Host", StringComparison.Ordinal)))
            .Where(exception => exception.Failures.Any(failure => failure.Contains("DiscoveryPrefix", StringComparison.Ordinal)))
            .Where(exception => exception.Failures.Any(failure => failure.Contains("MaxReconnectDelaySeconds", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Startup_ResolvesSecretFilesWithoutExposingValuesInStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hvo-mqtt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "mqtt-user"), "private-user");
            await File.WriteAllTextAsync(Path.Combine(root, "mqtt-password"), "private-password");
            var options = Options.Create(TestSupport.Options());
            var resolver = new SecretFileResolver(Options.Create(new EdgePathOptions { SecretsDirectory = root }));
            var credential = new MqttRuntimeCredential();
            var initializer = new HomeAssistantMqttInitializer(options, TestSupport.Identity(), resolver, credential);
            var projection = new HomeAssistantMqttProjection(TestSupport.Identity(), options);

            await initializer.StartingAsync(CancellationToken.None);

            credential.Settings!.Username.Should().Be("private-user");
            credential.Settings.Password.Should().Be("private-password");
            projection.GetStatus().ToString().Should().NotContain("private-user").And.NotContain("private-password");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Startup_RejectsMissingSecretFileAndMissingSiteIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hvo-mqtt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = Options.Create(TestSupport.Options());
            var resolver = new SecretFileResolver(Options.Create(new EdgePathOptions { SecretsDirectory = root }));
            var missingSecret = new HomeAssistantMqttInitializer(options, TestSupport.Identity(), resolver, new());
            var missingSite = new HomeAssistantMqttInitializer(options, TestSupport.Identity(siteId: null), resolver, new());

            var secretAct = () => missingSecret.StartingAsync(CancellationToken.None);
            var siteAct = () => missingSite.StartingAsync(CancellationToken.None);

            await secretAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
            await siteAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SiteId is required*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Registration_AddsProjectionAndHostedServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddSingleton(TestSupport.Identity());
        services.AddSingleton(new SecretFileResolver(Options.Create(new EdgePathOptions())));

        services.AddHvoHomeAssistantMqtt(configuration);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IHomeAssistantMqttProjection>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Should().Contain(service => service is HomeAssistantMqttInitializer);
        provider.GetServices<IHostedService>().Should().Contain(service => service is HomeAssistantMqttWorker);
    }

    private static ServiceProvider CreateOptionsProvider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddOptions<HomeAssistantMqttOptions>()
            .Bind(configuration.GetSection(HomeAssistantMqttOptions.SectionName))
            .ValidateDataAnnotations();
        services.AddSingleton<IValidateOptions<HomeAssistantMqttOptions>, HomeAssistantMqttOptionsValidator>();
        return services.BuildServiceProvider();
    }
}

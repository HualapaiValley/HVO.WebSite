using FluentAssertions;
using HVO.Edge.Hosting.Configuration;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.WeatherUnderground;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.Configuration;

[TestClass]
public sealed class WeatherUndergroundOptionsValidatorTests
{
    private readonly WeatherUndergroundOptionsValidator validator = new();

    [TestMethod]
    public void Disabled_DoesNotRequirePublicationSettings()
    {
        var result = validator.Validate(null, new WeatherUndergroundOptions
        {
            Enabled = false,
            StationId = "",
            StationKeySecret = "",
        });

        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public void Enabled_RequiresSafeStationAndSecretSettings()
    {
        var result = validator.Validate(null, new WeatherUndergroundOptions
        {
            Enabled = true,
            StationId = "station id",
            StationKeySecret = "../station-key",
            IntervalSeconds = 5,
            RequestTimeoutSeconds = 3,
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task Initializer_Disabled_DoesNotReadSecretFile()
    {
        var credential = new WeatherUndergroundCredential();
        var resolver = new SecretFileResolver(Options.Create(new EdgePathOptions
        {
            SecretsDirectory = Path.Combine(Path.GetTempPath(), $"missing-wu-secrets-{Guid.NewGuid():N}"),
        }));
        var initializer = new WeatherUndergroundInitializer(
            Options.Create(new WeatherUndergroundOptions { Enabled = false }),
            resolver,
            credential);

        await initializer.StartingAsync(CancellationToken.None);

        var action = credential.GetRequired;
        action.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Initializer_Enabled_RejectsMissingSecretWithoutExposingPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"missing-wu-secrets-{Guid.NewGuid():N}");
        var credential = new WeatherUndergroundCredential();
        var resolver = new SecretFileResolver(Options.Create(new EdgePathOptions { SecretsDirectory = root }));
        var initializer = new WeatherUndergroundInitializer(
            Options.Create(new WeatherUndergroundOptions { Enabled = true }),
            resolver,
            credential);

        var action = () => initializer.StartingAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("WeatherUnderground:StationKeySecret");
        exception.Which.Message.Should().NotContain(root);
    }
}

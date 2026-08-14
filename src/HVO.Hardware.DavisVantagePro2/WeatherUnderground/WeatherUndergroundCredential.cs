using HVO.Edge.Hosting.Configuration;
using HVO.Hardware.DavisVantagePro2.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.WeatherUnderground;

internal sealed class WeatherUndergroundCredential
{
    private string? stationKey;

    public void Initialize(string value) => stationKey = value;

    public string GetRequired() => stationKey
        ?? throw new InvalidOperationException("The Weather Underground station credential has not been initialized.");
}

internal sealed class WeatherUndergroundInitializer(
    IOptions<WeatherUndergroundOptions> options,
    SecretFileResolver secretResolver,
    WeatherUndergroundCredential credential) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            credential.Initialize(secretResolver.ReadRequired(
                options.Value.StationKeySecret,
                "WeatherUnderground:StationKeySecret"));
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

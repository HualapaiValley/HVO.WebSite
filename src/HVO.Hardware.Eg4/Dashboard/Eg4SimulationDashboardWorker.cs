using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Simulation;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Dashboard;

public sealed class Eg4SimulationDashboardWorker(
    Eg4FleetSimulator simulator,
    IOptions<Eg4Options> options,
    IEg4GatewayDashboardPublisher publisher,
    TimeProvider timeProvider,
    ILogger<Eg4SimulationDashboardWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = (options.Value.Devices ?? [])
            .Where(device => device.Enabled)
            .Select(device => PollDeviceLoopAsync(device, stoppingToken));
        await Task.WhenAll(loops);
    }

    public async Task PublishOnceAsync(CancellationToken cancellationToken)
    {
        var devices = (options.Value.Devices ?? []).Where(device => device.Enabled).ToArray();
        var results = await simulator.PollFleetAsync(devices, cancellationToken);
        foreach (var result in results)
        {
            if (result.Observation is not null)
                publisher.Publish(result.Device, result.Observation);
            else if (result.Error is not null)
            {
                publisher.PublishFailure(result.Device, result.Error);
                logger.LogWarning("EG4 simulation read failed for source {SourceId} with {FailureType}",
                    result.Device.SourceId, result.Error.GetType().Name);
            }
        }
    }

    private async Task PollDeviceLoopAsync(Eg4DeviceOptions device, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                publisher.Publish(device, await simulator.ReadAsync(device, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                publisher.PublishFailure(device, exception);
                logger.LogWarning("EG4 simulation read failed for source {SourceId} with {FailureType}",
                    device.SourceId, exception.GetType().Name);
            }
            var interval = TimeSpan.FromSeconds(device.PollIntervalSeconds ?? options.Value.DefaultPollIntervalSeconds);
            await Task.Delay(interval, timeProvider, cancellationToken);
        }
    }
}

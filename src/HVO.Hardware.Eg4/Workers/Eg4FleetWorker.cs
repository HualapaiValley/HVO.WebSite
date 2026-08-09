using System.Diagnostics;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using HVO.Hardware.Eg4.Outbox;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Workers;

public sealed class Eg4FleetWorker(
    IEg4TelemetrySource telemetrySource,
    IServiceScopeFactory scopeFactory,
    IOptions<Eg4Options> options,
    IEg4GatewayDashboardPublisher dashboard,
    GatewayTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<Eg4FleetWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var devices = (options.Value.Devices ?? [])
            .Where(device => device.Enabled && telemetrySource.Supports(device.Type))
            .ToArray();
        if (devices.Length == 0)
        {
            logger.LogWarning("No enabled validated EG4 6500EX devices are configured; collection is disabled.");
            return;
        }

        logger.LogInformation("EG4 fleet worker starting for {DeviceCount} validated device(s)", devices.Length);
        await Task.WhenAll(devices.Select(device => PollDeviceLoopAsync(device, stoppingToken)));
    }

    internal async Task<bool> PollOnceAsync(Eg4DeviceOptions device, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var observation = await telemetrySource.ReadAsync(device, cancellationToken);
            dashboard.Publish(device, observation);
            telemetry.RecordPoll(true, stopwatch.Elapsed.TotalSeconds,
                device.SourceId, device.DeviceId, "eg4-6500ex");

            var enqueueAttempt = 0;
            while (true)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var writer = scope.ServiceProvider.GetRequiredService<IEg4PowerOutboxWriter>();
                    await writer.EnqueueAsync(Eg4PowerReadingMapper.Map(observation), cancellationToken);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    enqueueAttempt++;
                    logger.LogWarning(exception, "EG4 observation enqueue attempt {Attempt} failed for source {SourceId}; the same observation will be retried",
                        enqueueAttempt, device.SourceId);
                    var delaySeconds = Math.Min(1 << Math.Min(enqueueAttempt - 1, 5), 30);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), timeProvider, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            dashboard.PublishFailure(device, exception);
            var failureKind = exception is Eg4TransportException transport
                ? transport.Kind.ToString().ToLowerInvariant()
                : "device_error";
            telemetry.RecordPoll(false, stopwatch.Elapsed.TotalSeconds,
                device.SourceId, device.DeviceId, "eg4-6500ex", failureKind);
            logger.LogWarning("EG4 poll failed for source {SourceId} with {FailureKind}", device.SourceId, failureKind);
            return false;
        }
    }

    private async Task PollDeviceLoopAsync(Eg4DeviceOptions device, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(device.PollIntervalSeconds ?? options.Value.DefaultPollIntervalSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            await PollOnceAsync(device, cancellationToken);
            await Task.Delay(interval, timeProvider, cancellationToken);
        }
    }
}

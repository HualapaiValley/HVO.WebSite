using System.Diagnostics;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Diagnostics;
using HVO.Hardware.Eg4.HomeAssistant;
using HVO.Hardware.Eg4.Outbox;
using HVO.Hardware.Eg4.Protocol;
using HVO.Hardware.Eg4.Simulation;
using HVO.Hardware.Eg4.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Workers;

internal sealed class Eg4FleetWorker(
    IEg4TelemetrySource telemetrySource,
    IServiceScopeFactory scopeFactory,
    IOptions<Eg4Options> options,
    Eg4RuntimeState runtimeState,
    Eg4HomeAssistantProjection homeAssistant,
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
            logger.LogWarning("No enabled validated EG4 devices are configured; collection is disabled.");
            return;
        }

        if (telemetrySource is Eg4FleetSimulator simulator)
        {
            for (var index = 0; index < devices.Length; index++)
            {
                var currentA = 6d + index * 2;
                await simulator.SeedIfUnscriptedAsync(devices[index].SourceId,
                    new Eg4SimulatedTelemetry(52.4, currentA, 52.4 * currentA, 80 - index * 2, IncludeRichDetail: true),
                    stoppingToken);
            }
        }

        logger.LogInformation("EG4 fleet worker starting for {DeviceCount} validated device(s)", devices.Length);
        await Task.WhenAll(devices.Select(device => PollDeviceLoopAsync(device, stoppingToken)));
    }

    internal async Task<bool> PollOnceAsync(Eg4DeviceOptions device, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var sample = await telemetrySource.ReadAsync(device, cancellationToken);
            if (!sample.IsAvailable || sample.BatteryObservation is null)
            {
                var attemptedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                runtimeState.RecordFailure(device, attemptedAtUtc, "unavailable");
                homeAssistant.PublishUnavailable(device, attemptedAtUtc);
                telemetry.RecordPoll(false, stopwatch.Elapsed.TotalSeconds,
                    device.SourceId, device.DeviceId, DeviceKind(device), "unavailable");
                return false;
            }
            var observation = sample.BatteryObservation;
            runtimeState.RecordSuccess(device, observation, timeProvider.GetUtcNow().UtcDateTime);
            homeAssistant.Publish(device, sample);
            telemetry.RecordPoll(true, stopwatch.Elapsed.TotalSeconds,
                device.SourceId, device.DeviceId, DeviceKind(device));

            var enqueueAttempt = 0;
            while (true)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var writer = scope.ServiceProvider.GetRequiredService<IEg4PowerOutboxWriter>();
                    await writer.EnqueueAsync(new Eg4ObservationBundle(
                        Eg4PowerReadingMapper.Map(observation),
                        sample.MpptDetail,
                        sample.InverterDetail), cancellationToken);
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
            var failureKind = exception is Eg4TransportException transport
                ? transport.Kind.ToString().ToLowerInvariant()
                : "device_error";
            var attemptedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            runtimeState.RecordFailure(device, attemptedAtUtc, failureKind);
            homeAssistant.PublishUnavailable(device, attemptedAtUtc);
            telemetry.RecordPoll(false, stopwatch.Elapsed.TotalSeconds,
                device.SourceId, device.DeviceId, DeviceKind(device), failureKind);
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

    private static string DeviceKind(Eg4DeviceOptions device) => device.Type == Eg4DeviceType.Inverter6500Ex
        ? "eg4-6500ex"
        : "eg4-mppt100-48hv";
}

using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.WeatherUnderground;

internal sealed class WeatherUndergroundPublisher(
    DavisRuntimeState davisState,
    WeatherUndergroundPublisherState publisherState,
    WeatherUndergroundClient client,
    WeatherUndergroundCredential credential,
    WeatherUndergroundMetrics metrics,
    IOptions<WeatherUndergroundOptions> options,
    TimeProvider timeProvider,
    ILogger<WeatherUndergroundPublisher> logger) : BackgroundService
{
    private const int MaximumAttempts = 2;
    private readonly WeatherUndergroundOptions configuration = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.Enabled)
            return;

        using var timer = new PeriodicTimer(configuration.Interval, timeProvider);
        await PublishLatestAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await PublishLatestAsync(stoppingToken).ConfigureAwait(false);
    }

    internal async Task PublishLatestAsync(CancellationToken cancellationToken)
    {
        WeatherUndergroundSendResult result = default;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var latest = davisState.GetLatestObservation();
            if (latest is null)
            {
                publisherState.Skipped(WeatherUndergroundOutcome.NoReading);
                metrics.RecordOutcome(configuration.StationId, WeatherUndergroundOutcome.NoReading);
                return;
            }

            publisherState.Observed(DateTime.SpecifyKind(latest.Observation.RecordedAtUtc, DateTimeKind.Utc));
            var now = timeProvider.GetUtcNow();
            if (now.UtcDateTime - latest.ReceivedAtUtc > configuration.StaleAfter)
            {
                publisherState.Skipped(WeatherUndergroundOutcome.StaleObservation);
                metrics.RecordOutcome(configuration.StationId, WeatherUndergroundOutcome.StaleObservation);
                return;
            }

            publisherState.Attempted(now.UtcDateTime);
            metrics.RecordAttempt(configuration.StationId);
            var started = timeProvider.GetTimestamp();
            result = await client.SendAsync(
                credential.GetRequired(),
                latest.Observation,
                cancellationToken).ConfigureAwait(false);
            metrics.RecordOutcome(
                configuration.StationId,
                result.Outcome,
                timeProvider.GetElapsedTime(started).TotalSeconds);

            if (result.Succeeded)
            {
                publisherState.Succeeded(timeProvider.GetUtcNow().UtcDateTime);
                logger.LogDebug(
                    "Weather Underground delivery for station {StationId} completed with outcome {Outcome}",
                    configuration.StationId,
                    result.Outcome.Category());
                return;
            }

            if (!result.Retryable || attempt == MaximumAttempts)
                break;
            await Task.Delay(WeatherUndergroundOptions.RetryBackoff, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        publisherState.Failed(result.Outcome);
        logger.LogWarning(
            "Weather Underground delivery for station {StationId} completed with outcome {Outcome} and category {FailureCategory}",
            configuration.StationId,
            "failure",
            result.Outcome.Category());
    }
}

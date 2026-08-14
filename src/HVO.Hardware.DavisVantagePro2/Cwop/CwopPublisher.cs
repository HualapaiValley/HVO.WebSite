using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Cwop;

internal sealed class CwopPublisher(
    DavisRuntimeState davisState,
    IStationSettingsSnapshotStore settingsStore,
    ICwopClient client,
    CwopCredential credential,
    CwopPublisherState publisherState,
    IOptions<CwopOptions> options,
    TimeProvider timeProvider,
    ILogger<CwopPublisher> logger) : BackgroundService
{
    private readonly CwopOptions configuration = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await RunIterationAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<TimeSpan> RunIterationAsync(CancellationToken cancellationToken)
    {
        var succeeded = await PublishLatestAsync(cancellationToken).ConfigureAwait(false);
        return succeeded ? configuration.Interval : Backoff(publisherState.Snapshot().ConsecutiveFailures);
    }

    internal async Task<bool> PublishLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var latest = davisState.GetLatestObservation();
            if (latest is null)
                return Fail(CwopOutcome.NoReading.Category());

            publisherState.Observed(DateTime.SpecifyKind(latest.Observation.RecordedAtUtc, DateTimeKind.Utc));
            var now = timeProvider.GetUtcNow();
            if (now.UtcDateTime - latest.ReceivedAtUtc > configuration.StaleAfter)
                return Fail(CwopOutcome.StaleObservation.Category());

            var storedSettings = await settingsStore.GetAsync(cancellationToken).ConfigureAwait(false);
            if (storedSettings is null)
                return Fail(CwopOutcome.MissingSettings.Category());
            var positionError = CwopPacketFormatter.ValidatePosition(storedSettings.Settings);
            if (positionError is not null)
                return Fail($"{CwopOutcome.InvalidPosition.Category()}:{positionError}");

            publisherState.Attempted(now.UtcDateTime);
            var result = await client.SendAsync(
                CwopPacketFormatter.FormatLogin(
                    configuration.StationId,
                    credential.GetRequired(),
                    configuration.SoftwareName,
                    configuration.SoftwareVersion),
                CwopPacketFormatter.FormatPacket(configuration.StationId, latest.Observation, storedSettings.Settings),
                cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                publisherState.Succeeded(timeProvider.GetUtcNow().UtcDateTime);
                logger.LogInformation(
                    "CWOP delivery for station {StationId} to {Host} succeeded",
                    configuration.StationId,
                    configuration.Host);
                return true;
            }

            return Fail(result.Outcome.Category());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "CWOP delivery for station {StationId} to {Host} failed unexpectedly",
                configuration.StationId,
                configuration.Host);
            return Fail("publisher-error");
        }
    }

    private bool Fail(string category)
    {
        publisherState.Failed(category);
        logger.LogWarning(
            "CWOP delivery for station {StationId} to {Host} failed with category {FailureCategory}",
            configuration.StationId,
            configuration.Host,
            category);
        return false;
    }

    private TimeSpan Backoff(int failures)
    {
        var exponent = Math.Min(Math.Max(failures - 1, 0), 5);
        return TimeSpan.FromSeconds(Math.Min(3600, configuration.Interval.TotalSeconds * Math.Pow(2, exponent)));
    }
}

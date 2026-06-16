using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Outbox;
using HVO.Gateway.TplinkKasa.Telemetry;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace HVO.Gateway.TplinkKasa.Hosting;

public sealed class KasaGatewayWorker(
    IOptions<KasaGatewayOptions> options,
    KasaDeviceRegistry registry,
    KasaDevicePoller poller,
    KasaDeviceInteractionState interactionState,
    KasaGatewayState state,
    KasaGatewayTelemetry telemetry,
    IServiceScopeFactory scopeFactory,
    ILogger<KasaGatewayWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, DevicePollLoop> deviceLoops = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TP-Link/Kasa gateway worker starting.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileDeviceLoopsAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await StopAllDeviceLoopsAsync().ConfigureAwait(false);
            logger.LogInformation("TP-Link/Kasa gateway worker stopped.");
        }
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        state.MarkPollStarted();

        try
        {
            await PollDevicesOnceAsync(await registry.GetEnabledDevicesAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            state.MarkPollCompleted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.MarkPollFailed("Gateway polling failed.");
            logger.LogError(ex, "TP-Link/Kasa gateway polling failed.");
        }
    }

    private async Task ReconcileDeviceLoopsAsync(CancellationToken cancellationToken)
    {
        var devices = await registry.GetEnabledDevicesAsync(cancellationToken).ConfigureAwait(false);
        var configuredSourceIds = devices.Select(device => device.EffectiveSourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceId in deviceLoops.Keys)
        {
            if (!configuredSourceIds.Contains(sourceId) && deviceLoops.TryRemove(sourceId, out var removedLoop))
            {
                await StopDeviceLoopAsync(sourceId, removedLoop).ConfigureAwait(false);
            }
        }

        foreach (var device in devices)
        {
            var sourceId = device.EffectiveSourceId;
            var configSignature = CreateDeviceLoopSignature(device);
            if (deviceLoops.TryGetValue(sourceId, out var existingLoop))
            {
                if (string.Equals(existingLoop.ConfigSignature, configSignature, StringComparison.Ordinal))
                {
                    continue;
                }

                if (deviceLoops.TryRemove(sourceId, out var staleLoop))
                {
                    await StopDeviceLoopAsync(sourceId, staleLoop).ConfigureAwait(false);
                }
            }

            deviceLoops.TryAdd(sourceId, StartDeviceLoop(device, configSignature, cancellationToken));
        }
    }

    private DevicePollLoop StartDeviceLoop(KasaDeviceConfig device, string configSignature, CancellationToken workerCancellationToken)
    {
        var loopCts = CancellationTokenSource.CreateLinkedTokenSource(workerCancellationToken);
        var loopTask = RunDeviceLoopAsync(device, loopCts.Token);
        logger.LogInformation("TP-Link/Kasa device poll loop started for {SourceId}.", device.EffectiveSourceId);
        return new DevicePollLoop(loopCts, loopTask, configSignature);
    }

    private async Task RunDeviceLoopAsync(KasaDeviceConfig device, CancellationToken cancellationToken)
    {
        var intervalSeconds = device.PollIntervalSeconds.GetValueOrDefault(options.Value.PollIntervalSeconds);
        var interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
        var fullDetailsInterval = GetFullDetailsRefreshInterval();
        var nextFullDetailsRefreshAtUtc = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(interval);
        Task? activePoll = null;

        try
        {
            activePoll = TryStartDevicePoll(device, activePoll, interval, ShouldReadFullDetails, cancellationToken);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                activePoll = TryStartDevicePoll(device, activePoll, interval, ShouldReadFullDetails, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            state.MarkPollFailed($"Device polling failed for {device.EffectiveSourceId}.");
            logger.LogError(ex, "TP-Link/Kasa device poll loop failed for {SourceId}.", device.EffectiveSourceId);
        }
        finally
        {
            if (activePoll is not null)
            {
                await activePoll.ConfigureAwait(false);
            }

            logger.LogInformation("TP-Link/Kasa device poll loop stopped for {SourceId}.", device.EffectiveSourceId);
        }

        bool ShouldReadFullDetails()
        {
            if (fullDetailsInterval is null || DateTimeOffset.UtcNow < nextFullDetailsRefreshAtUtc)
            {
                return false;
            }

            nextFullDetailsRefreshAtUtc = DateTimeOffset.UtcNow.Add(fullDetailsInterval.Value);
            return true;
        }
    }

    private Task TryStartDevicePoll(KasaDeviceConfig device, Task? activePoll, TimeSpan interval, Func<bool> shouldReadFullDetails, CancellationToken cancellationToken)
    {
        if (interactionState.IsSuspended(device.EffectiveSourceId))
        {
            telemetry.DevicePollSkippedCount.Add(1, PollTags(device));
            logger.LogDebug("Skipping TP-Link/Kasa device {SourceId} poll tick because the device is being edited.", device.EffectiveSourceId);
            return activePoll ?? Task.CompletedTask;
        }

        if (activePoll is { IsCompleted: false })
        {
            telemetry.DevicePollSkippedCount.Add(1, PollTags(device));
            logger.LogWarning(
                "Skipping TP-Link/Kasa device {SourceId} poll tick because the previous poll is still running. IntervalMs={IntervalMs}",
                device.EffectiveSourceId,
                interval.TotalMilliseconds);
            return activePoll;
        }

        if (activePoll?.IsFaulted == true)
        {
            _ = activePoll.Exception;
        }

        var readFullDetails = shouldReadFullDetails();
        return RunDevicePollAttemptAsync(device, interval, readFullDetails, cancellationToken);
    }

    private async Task RunDevicePollAttemptAsync(KasaDeviceConfig device, TimeSpan interval, bool readFullDetails, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        state.MarkPollStarted();

        try
        {
            var result = await PollDeviceOnceAsync(device, readFullDetails, cancellationToken).ConfigureAwait(false);
            state.MarkPollCompleted();
            stopwatch.Stop();

            var resultName = result.IsSuccess
                ? result.IsDegraded ? "degraded" : "success"
                : "failed";
            RecordPoll(device, resultName, stopwatch.Elapsed);

            if (stopwatch.Elapsed > interval)
            {
                logger.LogWarning(
                    "TP-Link/Kasa device {SourceId} poll completed in {ElapsedMs:0} ms, exceeding interval {IntervalMs:0} ms. FullDetails={FullDetails} Result={PollResult}",
                    device.EffectiveSourceId,
                    stopwatch.Elapsed.TotalMilliseconds,
                    interval.TotalMilliseconds,
                    readFullDetails,
                    resultName);
            }
            else
            {
                logger.LogInformation(
                    "TP-Link/Kasa device {SourceId} poll completed in {ElapsedMs:0} ms. IntervalMs={IntervalMs:0} FullDetails={FullDetails} Result={PollResult}",
                    device.EffectiveSourceId,
                    stopwatch.Elapsed.TotalMilliseconds,
                    interval.TotalMilliseconds,
                    readFullDetails,
                    resultName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordPoll(device, "exception", stopwatch.Elapsed);
            state.MarkPollFailed($"Device polling failed for {device.EffectiveSourceId}.");
            logger.LogError(
                ex,
                "TP-Link/Kasa device {SourceId} poll attempt failed in {ElapsedMs:0} ms. IntervalMs={IntervalMs:0}",
                device.EffectiveSourceId,
                stopwatch.Elapsed.TotalMilliseconds,
                interval.TotalMilliseconds);
        }
    }

    private async Task PollDevicesOnceAsync(IReadOnlyList<KasaDeviceConfig> devices, CancellationToken cancellationToken)
    {
        var maxConcurrency = Math.Clamp(options.Value.MaxPollConcurrency, 1, 64);
        await Parallel.ForEachAsync(devices.Where(device => !interactionState.IsSuspended(device.EffectiveSourceId)), new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxConcurrency
        }, async (device, token) => await PollDeviceOnceAsync(device, token).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task<KasaPollResult> PollDeviceOnceAsync(KasaDeviceConfig device, CancellationToken cancellationToken)
        => await PollDeviceOnceAsync(device, readFullDetails: false, cancellationToken).ConfigureAwait(false);

    private async Task<KasaPollResult> PollDeviceOnceAsync(KasaDeviceConfig device, bool readFullDetails, CancellationToken cancellationToken)
    {
        var result = readFullDetails
            ? await poller.PollReadOnlyAsync(device, options.Value.DefaultPort, cancellationToken).ConfigureAwait(false)
            : await poller.PollStatusAsync(device, options.Value.DefaultPort, cancellationToken).ConfigureAwait(false);

        if (interactionState.IsSuspended(device.EffectiveSourceId))
        {
            logger.LogDebug("Discarding TP-Link/Kasa device {SourceId} poll result because the device is being edited.", device.EffectiveSourceId);
            return result;
        }

        state.ApplyResult(device, result);

        if (result is { IsSuccess: true, IsDegraded: false, Snapshot.IsOnline: true })
            await EnqueueOutboxAsync(device, result.Snapshot, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "TP-Link/Kasa device {SourceId} poll failed: {FailureReason}",
                device.EffectiveSourceId,
                result.FailureReason);
        }
        else if (result.IsDegraded)
        {
            logger.LogWarning(
                "TP-Link/Kasa device {SourceId} poll degraded: {DegradedReason}",
                device.EffectiveSourceId,
                result.DegradedReason);
        }

        return result;
    }

    private async Task EnqueueOutboxAsync(KasaDeviceConfig config, KasaDeviceSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            await using var outboxScope = scopeFactory.CreateAsyncScope();
            var outboxWriter = outboxScope.ServiceProvider.GetRequiredService<KasaOutboxWriter>();

            if (snapshot.Energy is not null)
                await outboxWriter.EnqueueEnergyAsync(snapshot, ct).ConfigureAwait(false);

            await outboxWriter.EnqueueInventoryAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbox enqueue failed for {SourceId}", config.EffectiveSourceId);
        }
    }

    private TimeSpan? GetFullDetailsRefreshInterval()
    {
        var intervalSeconds = options.Value.FullDetailsRefreshIntervalSeconds;
        return intervalSeconds is null ? null : TimeSpan.FromSeconds(Math.Max(60, intervalSeconds.Value));
    }

    private void RecordPoll(KasaDeviceConfig device, string result, TimeSpan elapsed)
    {
        var tags = PollTags(device, result);
        telemetry.DevicePollCount.Add(1, tags);
        telemetry.DevicePollDurationMs.Record(elapsed.TotalMilliseconds, tags);
    }

    private static KeyValuePair<string, object?>[] PollTags(KasaDeviceConfig device, string? result = null)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("device", device.EffectiveSourceId),
            new("kind", device.DeviceKind.ToString())
        };

        if (!string.IsNullOrWhiteSpace(result))
        {
            tags.Add(new("result", result));
        }

        return tags.ToArray();
    }

    private string CreateDeviceLoopSignature(KasaDeviceConfig device) => string.Join('|',
        device.Host,
        device.EffectivePort(options.Value.DefaultPort).ToString(System.Globalization.CultureInfo.InvariantCulture),
        device.PollIntervalSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        device.DisplayName,
        device.GroupName,
        device.IsFavorite.ToString(System.Globalization.CultureInfo.InvariantCulture),
        device.DisplayTimeZoneId,
        device.ExpectedModel,
        device.ExpectedHardwareVersion,
        device.ExpectedSoftwareVersion,
        device.ExpectedChildCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        device.ProtocolFamily.ToString(),
        device.DeviceKind.ToString(),
        device.SafetyClass.ToString(),
        string.Join(',', device.Capabilities.OrderBy(capability => capability.ToString())),
        string.Join(',', device.MetadataCapabilities.OrderBy(capability => capability.ToString())),
        string.Join(',', device.CommandCapabilities.OrderBy(capability => capability.ToString())));

    private async Task StopAllDeviceLoopsAsync()
    {
        foreach (var (sourceId, loop) in deviceLoops.ToArray())
        {
            if (deviceLoops.TryRemove(sourceId, out _))
            {
                await StopDeviceLoopAsync(sourceId, loop).ConfigureAwait(false);
            }
        }
    }

    private async Task StopDeviceLoopAsync(string sourceId, DevicePollLoop loop)
    {
        await loop.Cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            loop.Cancellation.Dispose();
            logger.LogInformation("TP-Link/Kasa device poll loop removed for {SourceId}.", sourceId);
        }
    }

    private sealed record DevicePollLoop(CancellationTokenSource Cancellation, Task Task, string ConfigSignature);
}

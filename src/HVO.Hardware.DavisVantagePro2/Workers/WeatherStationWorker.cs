using HVO.Edge.Contracts.Weather;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.HomeAssistant;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Workers;

public sealed class WeatherStationWorker(
    IDavisStation station,
    IServiceScopeFactory scopeFactory,
    IDavisArchiveCursorStore cursorStore,
    IStationSettingsSnapshotStore settingsStore,
    IStationInfoSnapshotStore infoStore,
    IDavisHomeAssistantProjection homeAssistant,
    DavisRuntimeState state,
    IOptions<StationOptions> options,
    TimeProvider timeProvider,
    ILogger<WeatherStationWorker> logger) : BackgroundService
{
    private const int LoopBatchSize = 30;
    private readonly StationOptions configuration = options.Value;
    private readonly SemaphoreSlim archiveGate = new(1, 1);
    private DateTime nextArchiveTopOffAtUtc = DateTime.MinValue;
    private Loop2Packet? loop1Cache;

    public Loop2Packet? LatestReading => state.LatestReading;
    public DateTime? LastReadingAt => state.LastReadingAtUtc;
    public int ConsecutiveErrors => state.ConsecutiveErrors;
    public string? LastError => state.LastError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconnectAttempts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await station.ConnectAsync(stoppingToken);
                reconnectAttempts = 0;
                state.Connected();
                await RefreshPersistedStationMetadataAsync(stoppingToken);
                await TryRunArchiveTopOffAsync(stoppingToken);
                await PollLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                state.Failed(exception.Message);
                homeAssistant.PublishUnavailable(timeProvider.GetUtcNow());
                reconnectAttempts++;
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, reconnectAttempts - 1)));
                logger.LogWarning(exception, "Davis station connection failed; retrying in {Delay}", delay);
                try
                {
                    await station.DisconnectAsync();
                    await Task.Delay(delay, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task RefreshPersistedStationMetadataAsync(CancellationToken cancellationToken)
    {
        var settings = await station.GetStationSettingsAsync(cancellationToken);
        station.ApplyStationSettings(settings);
        await settingsStore.SaveAsync(settings, cancellationToken);
        try
        {
            await infoStore.SaveAsync(await station.GetStationInfoAsync(cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Davis station information refresh failed; collection will continue");
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollLoopBatchAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                state.Failed(exception.Message);
                if (loop1Cache is null || state.ConsecutiveErrors >= configuration.MaxConsecutiveErrors)
                    throw;
                logger.LogWarning(exception, "Davis LOOP stream failed ({ConsecutiveErrors} consecutive)", state.ConsecutiveErrors);
            }

            await RunPeriodicArchiveTopOffIfDueAsync(cancellationToken);
        }
    }

    internal async Task<Loop2Packet> PollLoopBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            loop1Cache = await station.GetLoop1Async(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (loop1Cache is not null)
        {
            logger.LogWarning(exception, "Davis LOOP1 refresh failed; merging fresh LOOP2 packets with cached LOOP1 fields");
        }

        var loop1 = loop1Cache ?? throw new InvalidOperationException("Davis LOOP1 data is unavailable.");
        await foreach (var loop2 in station.StreamLoop2Async(LoopBatchSize, cancellationToken))
        {
            var reading = VantageStation.MergePackets(loop1, loop2);
            await EnqueueLiveAsync(reading, cancellationToken);
            state.Observed(reading, timeProvider.GetUtcNow().UtcDateTime);
            homeAssistant.Publish(reading);
        }
        return loop1;
    }

    internal async Task<bool> RunPeriodicArchiveTopOffIfDueAsync(CancellationToken cancellationToken)
    {
        if (configuration.ArchiveCatchupMode == ArchiveCatchupMode.Disabled
            || timeProvider.GetUtcNow().UtcDateTime < nextArchiveTopOffAtUtc)
            return false;

        await TryRunArchiveTopOffAsync(cancellationToken);
        return true;
    }

    private async Task TryRunArchiveTopOffAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunArchiveTopOffAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var attemptedAt = timeProvider.GetUtcNow().UtcDateTime;
            state.ArchiveTopOffFailed(attemptedAt, exception.Message);
            nextArchiveTopOffAtUtc = attemptedAt.AddSeconds(Math.Max(60, station.ArchiveIntervalSeconds));
            logger.LogWarning(exception, "Davis archive top-off failed; live LOOP collection will continue");
        }
    }

    public async Task<int> RunArchiveTopOffAsync(CancellationToken cancellationToken = default)
    {
        if (configuration.ArchiveCatchupMode == ArchiveCatchupMode.Disabled)
            return 0;
        await archiveGate.WaitAsync(cancellationToken);
        try
        {
            var cursor = await cursorStore.GetAsync(configuration.StationId, cancellationToken);
            DateTime sinceLocal;
            if (cursor is null)
            {
                sinceLocal = DateTime.MinValue;
                logger.LogInformation(
                    "No Davis archive cursor exists for {StationId}; requesting the full available console archive",
                    configuration.StationId);
            }
            else
            {
                var overlap = TimeSpan.FromSeconds(
                    Math.Max(60, station.ArchiveIntervalSeconds) * configuration.ArchiveOverlapIntervals);
                sinceLocal = cursor.ConsoleRecordedAtLocal - overlap;
                logger.LogInformation(
                    "Davis archive top-off from {SinceLocal} with {Overlap} overlap; cursor UTC is {CursorUtc}",
                    sinceLocal, overlap, cursor.RecordedAtUtc);
            }

            var count = 0;
            await foreach (var record in station.GetArchiveSinceAsync(
                sinceLocal,
                fallbackOnEmpty: true,
                cancellationToken: cancellationToken))
            {
                var local = DateTime.SpecifyKind(record.DateTimeLocal, DateTimeKind.Unspecified);
                var utc = new DateTimeOffset(local, station.ConsoleUtcOffset).UtcDateTime;
                await using var scope = scopeFactory.CreateAsyncScope();
                var writer = scope.ServiceProvider.GetRequiredService<IDavisOutboxWriter>();
                await writer.EnqueueArchiveAsync(MapArchive(record, local, utc), cancellationToken);
                await cursorStore.AdvanceAsync(configuration.StationId, local, utc, cancellationToken);
                count++;
            }
            state.ArchiveTopOffCompleted(timeProvider.GetUtcNow().UtcDateTime, count);
            nextArchiveTopOffAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(
                Math.Max(60, station.ArchiveIntervalSeconds));
            return count;
        }
        finally
        {
            archiveGate.Release();
        }
    }

    private async Task EnqueueLiveAsync(Loop2Packet reading, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDavisOutboxWriter>()
            .EnqueueRawAsync(MapLive(reading), cancellationToken);
    }

    private DavisWeatherLivePayload MapLive(Loop2Packet reading) => new()
    {
        StationId = configuration.StationId,
        RecordedAtUtc = DateTime.SpecifyKind(reading.RecordedAtUtc, DateTimeKind.Utc),
        TemperatureF = reading.OutsideTemperatureF,
        InsideTemperatureF = reading.InsideTemperatureF,
        DewPointF = reading.DewPointF,
        HeatIndexF = reading.HeatIndexF,
        WindChillF = reading.WindChillF,
        ThswF = reading.ThswF,
        HumidityPercent = reading.OutsideHumidityPercent,
        InsideHumidityPercent = reading.InsideHumidityPercent,
        BarometricPressureInHg = reading.BarometricPressureInHg,
        PressureRawInHg = reading.PressureRawInHg,
        AltimeterInHg = reading.AltimeterInHg,
        BarometricTrend = reading.BarometricTrend,
        WindSpeedMph = reading.WindSpeedMph,
        WindDirectionDegrees = Degrees(reading.WindDirectionDegrees),
        WindSpeed10MinAvgMph = reading.WindSpeed10MinAvgMph,
        WindSpeed2MinAvgMph = reading.WindSpeed2MinAvgMph,
        WindGust10MinMph = reading.WindGust10MinMph,
        WindGust10MinDirectionDegrees = Degrees(reading.WindGust10MinDirectionDegrees),
        RainRateInchesPerHour = reading.RainRateInchesPerHour,
        DailyRainInches = reading.DailyRainInches,
        Rain15MinInches = reading.Rain15MinInches,
        HourRainInches = reading.HourRainInches,
        Rain24HourInches = reading.Rain24HourInches,
        StormRainInches = reading.StormRainInches,
        StormStartDate = reading.StormStartDate,
        MonthlyRainInches = reading.MonthlyRainInches,
        YearlyRainInches = reading.YearlyRainInches,
        SolarRadiationWm2 = reading.SolarRadiationWm2,
        UvIndex = reading.UvIndex,
        DailyEtInches = reading.DailyEtInches,
        MonthlyEtInches = reading.MonthlyEtInches,
        YearlyEtInches = reading.YearlyEtInches,
        ConsoleBatteryVoltage = reading.ConsoleBatteryVoltage,
        TransmitterBatteryStatus = reading.TransmitterBatteryStatus ?? 0,
        ForecastRule = reading.ForecastRule,
        ForecastString = reading.ForecastString,
        SunriseTime = reading.SunriseDisplay,
        SunsetTime = reading.SunsetDisplay,
    };

    private DavisWeatherArchivePayload MapArchive(ArchiveRecord record, DateTime local, DateTime utc) => new()
    {
        StationId = configuration.StationId,
        RecordedAtUtc = utc,
        ConsoleRecordedAtLocal = local,
        ArchiveIntervalMinutes = record.ArchiveIntervalMinutes,
        TemperatureF = record.OutsideTemperatureF,
        HighTemperatureF = record.HighOutsideTemperatureF,
        LowTemperatureF = record.LowOutsideTemperatureF,
        InsideTemperatureF = record.InsideTemperatureF,
        HumidityPercent = record.OutsideHumidityPercent,
        InsideHumidityPercent = record.InsideHumidityPercent,
        BarometricPressureInHg = record.BarometricPressureInHg,
        WindSpeedMph = record.WindSpeedMph,
        WindGustMph = record.WindGustMph,
        WindDirectionDegrees = record.WindDirectionDegrees,
        WindGustDirectionDegrees = record.WindGustDirectionDegrees,
        WindSamples = record.WindSamples,
        RainfallInches = record.RainInches,
        RainRateInchesPerHour = record.RainRateInchesPerHour,
        SolarRadiationWm2 = record.SolarRadiationWm2,
        HighSolarRadiationWm2 = record.HighSolarRadiationWm2,
        UvIndex = record.UvIndex,
        HighUvIndex = record.HighUvIndex,
        EtInches = record.EtInches,
        ForecastRule = record.ForecastRule,
        ForecastString = record.ForecastRule.HasValue ? DavisForecastTable.GetForecastString(record.ForecastRule.Value) : null,
        DownloadRecordType = record.DownloadRecordType,
        LeafTemp1F = record.LeafTemp1F,
        LeafTemp2F = record.LeafTemp2F,
        LeafWetnessScaled = record.LeafWetnessScaled,
        SoilTemperaturesF = record.SoilTemperaturesF,
        ExtraHumiditiesPercent = record.ExtraHumiditiesPercent,
        ExtraTemperaturesF = record.ExtraTemperaturesF,
        SoilMoisturesCb = record.SoilMoisturesCb,
    };

    private static int? Degrees(double? value) => value.HasValue ? (int)value.Value : null;
}

public sealed class DavisRuntimeState
{
    private readonly object sync = new();
    public Loop2Packet? LatestReading { get; private set; }
    public DateTime? LastReadingAtUtc { get; private set; }
    public DateTime? LastArchiveTopOffAtUtc { get; private set; }
    public int LastArchiveTopOffCount { get; private set; }
    public string? LastArchiveError { get; private set; }
    public int ConsecutiveErrors { get; private set; }
    public string? LastError { get; private set; }
    public bool IsConnected { get; private set; }

    public void Connected() { lock (sync) { IsConnected = true; ConsecutiveErrors = 0; LastError = null; } }
    public void Observed(Loop2Packet reading, DateTime receivedAtUtc) { lock (sync) { IsConnected = true; LatestReading = reading; LastReadingAtUtc = receivedAtUtc; ConsecutiveErrors = 0; LastError = null; } }
    public void Failed(string error) { lock (sync) { IsConnected = false; ConsecutiveErrors++; LastError = error; } }
    public void ArchiveTopOffCompleted(DateTime atUtc, int count) { lock (sync) { LastArchiveTopOffAtUtc = atUtc; LastArchiveTopOffCount = count; LastArchiveError = null; } }
    public void ArchiveTopOffFailed(DateTime atUtc, string error) { lock (sync) { LastArchiveTopOffAtUtc = atUtc; LastArchiveError = error; } }
}

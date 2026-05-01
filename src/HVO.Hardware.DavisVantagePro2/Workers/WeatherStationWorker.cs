using System.Text.Json;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Workers;

/// <summary>
/// Background service that continuously streams LOOP2 packets from the Davis console
/// and writes each reading to the SQLite outbox.
///
/// Uses the LPS 2 command to stream packets at the console's native ~2-second cadence,
/// keeping the TCP connection live. LOOP1-only fields (battery, forecast, sunrise/sunset)
/// are refreshed once per batch via a single LPS 1 1 request before each stream batch.
///
/// On startup, optionally runs DMPAFT to catch up archive records missed while offline.
/// </summary>
public sealed class WeatherStationWorker(
    VantageStation station,
    IServiceScopeFactory scopeFactory,
    IOptions<StationOptions> options,
    ILogger<WeatherStationWorker> logger) : BackgroundService
{
    private readonly StationOptions _options = options.Value;

    // Expose the latest reading and fire an event so subscribers update immediately
    public Loop2Packet? LatestReading { get; private set; }
    public DateTime? LastReadingAt { get; private set; }
    public int ConsecutiveErrors { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Raised on the worker thread each time a new reading is available.
    /// Subscribers (e.g. Blazor status page) should marshal to the UI thread via InvokeAsync.
    /// </summary>
    public event Action<Loop2Packet>? ReadingUpdated;

    /// <summary>
    /// Raised when the worker's health state changes: connected, disconnected, or error count updated.
    /// Lets the status page refresh the health section even when readings have stopped flowing.
    /// </summary>
    public event Action? WorkerStateChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("WeatherStationWorker starting. Connecting to {Host}:{Port}",
            _options.Host, _options.Port);

        int reconnectAttempts = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await station.ConnectAsync(stoppingToken);
                reconnectAttempts = 0; // Successful connect — reset backoff
                ConsecutiveErrors = 0;
                LastError = null;
                WorkerStateChanged?.Invoke();

                if (_options.ArchiveCatchupOnStartup)
                    await CatchUpArchiveAsync(stoppingToken);

                await PollLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reconnectAttempts++;
                LastError = ex.Message;
                WorkerStateChanged?.Invoke();
                // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 30s (capped)
                var delaySeconds = (int)Math.Min(30, Math.Pow(2, reconnectAttempts - 1));
                logger.LogError(ex, "Station error (consecutive: {N}). Reconnecting in {Delay}s…",
                    ConsecutiveErrors, delaySeconds);
                try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("WeatherStationWorker stopped");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        // Number of LOOP2 packets per LPS 2 command (~60 s at the console's native 2 s/packet
        // cadence). After each batch the console returns to command mode; LOOP1 is refreshed
        // and the next LPS command is issued immediately to keep the TCP connection live.
        const int BatchSize = 30;

        Loop2Packet? loop1Cache = null;

        while (!ct.IsCancellationRequested)
        {
            // Refresh LOOP1-only fields (battery, forecast, sunrise/sunset, monthly totals)
            // once per batch. Keeps the LOOP2 stream alive while limiting LOOP1 overhead.
            try
            {
                loop1Cache = await station.GetLoop1Async(ct);
                ConsecutiveErrors = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ConsecutiveErrors++;
                LastError = ex.Message;
                WorkerStateChanged?.Invoke();
                logger.LogWarning(ex, "LOOP1 refresh failed ({N} consecutive)", ConsecutiveErrors);
                if (loop1Cache is null || ConsecutiveErrors >= _options.MaxConsecutiveErrors)
                    throw; // No cached data or too many failures — trigger reconnect
            }

            // Stream LOOP2 packets. The console sends one every ~2 s; no sleep needed.
            try
            {
                await foreach (var loop2 in station.StreamLoop2Async(BatchSize, ct))
                {
                    Loop2Packet reading = VantageStation.MergePackets(loop1Cache!, loop2);
                    LatestReading = reading;
                    LastReadingAt = DateTime.UtcNow;
                    ConsecutiveErrors = 0;
                    ReadingUpdated?.Invoke(reading);

                    await WriteToOutboxAsync(reading, ct);
                    logger.LogDebug("LOOP2: {T:F1}°F, {H:F0}%RH, {P:F3} inHg",
                        reading.OutsideTemperatureF, reading.OutsideHumidityPercent, reading.BarometricPressureInHg);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ConsecutiveErrors++;
                LastError = ex.Message;
                WorkerStateChanged?.Invoke();
                logger.LogWarning(ex, "LOOP2 stream failed ({N} consecutive)", ConsecutiveErrors);
                if (ConsecutiveErrors >= _options.MaxConsecutiveErrors)
                    throw; // Trigger reconnect
            }
        }
    }

    private async Task CatchUpArchiveAsync(CancellationToken ct)
    {
        DateTime since = await GetLastArchivedTimeAsync(ct);
        logger.LogInformation("DMPAFT catchup since {Since}", since == DateTime.MinValue ? "beginning" : since.ToString("g"));

        int count = 0;
        await foreach (var rec in station.GetArchiveSinceAsync(since, ct: ct))
        {
            await WriteArchiveToOutboxAsync(rec, ct);
            count++;
        }

        logger.LogInformation("DMPAFT catchup complete: {Count} records", count);
    }

    private async Task<DateTime> GetLastArchivedTimeAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var last = await db.OutboxRecords
            .Where(r => r.IsArchiveRecord)
            .OrderByDescending(r => r.RecordedAtUtc)
            .Select(r => (DateTime?)r.RecordedAtUtc)
            .FirstOrDefaultAsync(ct);
        return last.HasValue ? last.Value.ToLocalTime() : DateTime.MinValue;
    }

    private async Task WriteToOutboxAsync(Loop2Packet reading, CancellationToken ct)
    {
        var payload = new
        {
            StationId = _options.StationId,
            RecordedAt = reading.RecordedAtUtc,
            // Temperature
            TemperatureF = reading.OutsideTemperatureF,
            InsideTemperatureF = reading.InsideTemperatureF,
            DewPointF = reading.DewPointF,
            HeatIndexF = reading.HeatIndexF,
            WindChillF = reading.WindChillF,
            ThswF = reading.ThswF,
            // Humidity
            HumidityPercent = reading.OutsideHumidityPercent,
            InsideHumidityPercent = reading.InsideHumidityPercent,
            // Barometer
            BarometricPressureInHg = reading.BarometricPressureInHg,
            PressureRawInHg = reading.PressureRawInHg,
            AltimeterInHg = reading.AltimeterInHg,
            BarometricTrend = reading.BarometricTrend,
            // Wind
            WindSpeedMph = reading.WindSpeedMph,
            WindDirectionDegrees = reading.WindDirectionDegrees.HasValue
                ? (int?)(int)reading.WindDirectionDegrees.Value : null,
            WindSpeed10MinAvgMph = reading.WindSpeed10MinAvgMph,
            WindSpeed2MinAvgMph = reading.WindSpeed2MinAvgMph,
            WindGust10MinMph = reading.WindGust10MinMph,
            WindGust10MinDirectionDegrees = reading.WindGust10MinDirectionDegrees.HasValue
                ? (int?)(int)reading.WindGust10MinDirectionDegrees.Value : null,
            // Rain
            RainRateInchesPerHour = reading.RainRateInchesPerHour,
            DailyRainInches = reading.DailyRainInches,
            Rain15MinInches = reading.Rain15MinInches,
            HourRainInches = reading.HourRainInches,
            Rain24HourInches = reading.Rain24HourInches,
            StormRainInches = reading.StormRainInches,
            StormStartDate = reading.StormStartDate,
            MonthlyRainInches = reading.MonthlyRainInches,
            YearlyRainInches = reading.YearlyRainInches,
            // Solar / UV / ET
            SolarRadiationWm2 = reading.SolarRadiationWm2,
            UvIndex = reading.UvIndex,
            DailyEtInches = reading.DailyEtInches,
            MonthlyEtInches = reading.MonthlyEtInches,
            YearlyEtInches = reading.YearlyEtInches,
            // Console status (LOOP1-sourced)
            ConsoleBatteryVoltage = reading.ConsoleBatteryVoltage,
            TransmitterBatteryStatus = reading.TransmitterBatteryStatus,
            ForecastRule = reading.ForecastRule,
            ForecastString = reading.ForecastString,
            SunriseTime = reading.SunriseDisplay,
            SunsetTime = reading.SunsetDisplay,
        };
        await EnqueueAsync(reading.RecordedAtUtc, JsonSerializer.Serialize(payload), isArchiveRecord: false, ct);
    }

    private async Task WriteArchiveToOutboxAsync(ArchiveRecord rec, CancellationToken ct)
    {
        // Convert the console's local time to UTC using the timezone configured in EEPROM.
        // Strip DateTimeKind.Local first: the console clock is not the host OS timezone,
        // and DateTimeOffset rejects a Local DateTime whose offset doesn't match the host offset.
        var consoleLocal = DateTime.SpecifyKind(rec.DateTimeLocal, DateTimeKind.Unspecified);
        var recordedAtUtc = new DateTimeOffset(consoleLocal, station.ConsoleUtcOffset).UtcDateTime;
        var payload = new
        {
            StationId = _options.StationId,
            RecordedAt = recordedAtUtc,
            ArchiveIntervalMinutes = rec.ArchiveIntervalMinutes,
            // Temperature
            TemperatureF = rec.OutsideTemperatureF,
            HighTemperatureF = rec.HighOutsideTemperatureF,
            LowTemperatureF = rec.LowOutsideTemperatureF,
            InsideTemperatureF = rec.InsideTemperatureF,
            // Humidity
            HumidityPercent = rec.OutsideHumidityPercent,
            InsideHumidityPercent = rec.InsideHumidityPercent,
            // Barometer
            BarometricPressureInHg = rec.BarometricPressureInHg,
            // Wind
            WindSpeedMph = rec.WindSpeedMph,
            WindGustMph = rec.WindGustMph,
            WindDirectionDegrees = rec.WindDirectionDegrees.HasValue
                ? (int?)(int)rec.WindDirectionDegrees.Value : null,
            WindGustDirectionDegrees = rec.WindGustDirectionDegrees.HasValue
                ? (int?)(int)rec.WindGustDirectionDegrees.Value : null,
            WindSamples = rec.WindSamples,
            // Rain
            RainfallInches = rec.RainInches,
            RainRateInchesPerHour = rec.RainRateInchesPerHour,
            // Solar / UV / ET
            SolarRadiationWm2 = rec.SolarRadiationWm2,
            HighSolarRadiationWm2 = rec.HighSolarRadiationWm2,
            UvIndex = rec.UvIndex,
            HighUvIndex = rec.HighUvIndex,
            EtInches = rec.EtInches,
            // Forecast
            ForecastRule = rec.ForecastRule,
            ForecastString = rec.ForecastRule.HasValue
                ? DavisForecastTable.GetForecastString(rec.ForecastRule.Value) : null,
            // Extra sensors (rec_B layout: bytes 34–51)
            LeafTemp1F = rec.LeafTemp1F,
            LeafTemp2F = rec.LeafTemp2F,
            LeafWetnessScaled = rec.LeafWetnessScaled,
            SoilTemperaturesF = rec.SoilTemperaturesF,
            ExtraHumiditiesPercent = rec.ExtraHumiditiesPercent,
            ExtraTemperaturesF = rec.ExtraTemperaturesF,
            SoilMoisturesCb = rec.SoilMoisturesCb,
        };
        await EnqueueAsync(recordedAtUtc, JsonSerializer.Serialize(payload), isArchiveRecord: true, ct);
    }

    private async Task EnqueueAsync(DateTime recordedAtUtc, string json, bool isArchiveRecord, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        // Idempotent: skip duplicates (DMPAFT re-runs produce same timestamp)
        bool exists = await db.OutboxRecords.AnyAsync(r => r.RecordedAtUtc == recordedAtUtc, ct);
        if (exists) return;

        db.OutboxRecords.Add(new OutboxRecord
        {
            RecordedAtUtc = recordedAtUtc,
            Payload = json,
            IsArchiveRecord = isArchiveRecord,
            Status = OutboxStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}

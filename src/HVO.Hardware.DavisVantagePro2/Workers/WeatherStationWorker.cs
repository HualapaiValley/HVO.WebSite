using System.Text.Json;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Outbox;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.Station;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Workers;

/// <summary>
/// Background service that polls the Davis console for LOOP2 packets on a
/// configurable interval and writes each reading to the SQLite outbox.
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

    // Expose the latest reading for the status page
    public Loop2Packet? LatestReading { get; private set; }
    public DateTime? LastReadingAt { get; private set; }
    public int ConsecutiveErrors { get; private set; }
    public string? LastError { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("WeatherStationWorker starting. Connecting to {Host}:{Port}",
            _options.Host, _options.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await station.ConnectAsync(stoppingToken);
                ConsecutiveErrors = 0;
                LastError = null;

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
                LastError = ex.Message;
                logger.LogError(ex, "Station error (consecutive: {N}). Reconnecting in 30s…", ConsecutiveErrors);
                try { await Task.Delay(30_000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        logger.LogInformation("WeatherStationWorker stopped");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(_options.PollingIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                Loop2Packet reading = await station.GetCurrentConditionsAsync(ct);
                LatestReading = reading;
                LastReadingAt = DateTime.UtcNow;
                ConsecutiveErrors = 0;

                await WriteToOutboxAsync(reading, ct);
                logger.LogDebug("LOOP2 read: {T:F1}°F, {H:F0}%RH, {P:F3} inHg",
                    reading.OutsideTemperatureF, reading.OutsideHumidityPercent, reading.BarometricPressureInHg);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ConsecutiveErrors++;
                LastError = ex.Message;
                logger.LogWarning(ex, "LOOP2 read failed ({N} consecutive)", ConsecutiveErrors);

                if (ConsecutiveErrors >= _options.MaxConsecutiveErrors)
                    throw; // Bubble up to reconnect logic
            }

            await Task.Delay(delay, ct);
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
            TemperatureF = reading.OutsideTemperatureF,
            HumidityPercent = reading.OutsideHumidityPercent,
            reading.DewPointF,
            reading.BarometricPressureInHg,
            reading.WindSpeedMph,
            WindGustMph = reading.WindGust10MinMph,
            WindDirectionDegrees = reading.WindDirectionDegrees.HasValue
                ? (int?)(int)reading.WindDirectionDegrees.Value : null,
            RainfallInches = reading.DailyRainInches,
            reading.SolarRadiationWm2,
            reading.UvIndex,
        };
        await EnqueueAsync(reading.RecordedAtUtc, JsonSerializer.Serialize(payload), isArchiveRecord: false, ct);
    }

    private async Task WriteArchiveToOutboxAsync(ArchiveRecord rec, CancellationToken ct)
    {
        var payload = new
        {
            StationId = _options.StationId,
            RecordedAt = rec.DateTimeLocal.ToUniversalTime(),
            TemperatureF = rec.OutsideTemperatureF,
            HumidityPercent = rec.OutsideHumidityPercent,
            rec.BarometricPressureInHg,
            rec.WindSpeedMph,
            WindGustMph = rec.WindGustMph,
            WindDirectionDegrees = rec.WindDirectionDegrees.HasValue
                ? (int?)(int)rec.WindDirectionDegrees.Value : null,
            RainfallInches = rec.RainInches,
            rec.SolarRadiationWm2,
            rec.UvIndex,
        };
        await EnqueueAsync(rec.DateTimeLocal.ToUniversalTime(), JsonSerializer.Serialize(payload), isArchiveRecord: true, ct);
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
            RecordedAtUtc    = recordedAtUtc,
            Payload          = json,
            IsArchiveRecord  = isArchiveRecord,
            Status           = OutboxStatus.Pending,
            CreatedAtUtc     = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}

using System.Linq.Expressions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

public sealed class V9WeatherQueryService(HvoV9DbContext db, TimeProvider timeProvider) : IV9WeatherQueryService
{
    public static readonly TimeSpan MaximumHistoryRange = TimeSpan.FromDays(31);
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromMinutes(5);
    public const int MaximumPageSize = 1000;

    private static readonly Expression<Func<WeatherRaw, V9WeatherObservation>> RawProjection = row => new()
    {
        Id = row.Id, StationId = row.StationId!, RecordedAtUtc = row.RecordedAt,
        TemperatureF = row.TemperatureF, HumidityPercent = row.HumidityPercent, DewPointF = row.DewPointF,
        BarometricPressureInHg = row.BarometricPressureInHg, WindSpeedMph = row.WindSpeedMph,
        WindGustMph = row.WindGustMph, WindDirectionDegrees = row.WindDirectionDegrees,
        RainfallInches = row.RainfallInches, SolarRadiationWm2 = row.SolarRadiationWm2, UvIndex = row.UvIndex
    };

    public async Task<V9WeatherObservation?> GetLatestAsync(string stationId, CancellationToken cancellationToken = default)
    {
        ValidateStation(stationId);
        cancellationToken.ThrowIfCancellationRequested();
        return await LatestAsync(stationId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public async Task<V9WeatherCurrent> GetCurrentAsync(string stationId, TimeSpan? staleAfter = null,
        CancellationToken cancellationToken = default)
    {
        ValidateStation(stationId);
        var threshold = staleAfter ?? DefaultStaleAfter;
        if (threshold <= TimeSpan.Zero || threshold > MaximumHistoryRange)
            throw new ArgumentOutOfRangeException(nameof(staleAfter), "Freshness must be positive and at most 31 days.");
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var observation = await LatestAsync(stationId, now, cancellationToken);
        var age = observation is null ? (TimeSpan?)null : now - observation.RecordedAtUtc;
        var availability = observation is null ? V9WeatherAvailability.NoData
            : age >= threshold ? V9WeatherAvailability.Stale : V9WeatherAvailability.Current;
        return new(stationId, now, availability, observation, age, threshold);
    }

    public async Task<V9WeatherHistoryPage<V9WeatherObservation>> GetRawHistoryAsync(string stationId,
        DateTimeOffset start, DateTimeOffset end, int limit = 100, DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        ValidateHistory(stationId, start, end, limit, after);
        cancellationToken.ThrowIfCancellationRequested();
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;
        var afterUtc = after?.UtcDateTime;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await db.WeatherRaw.AsNoTracking()
            .Where(row => row.StationId == stationId && row.RecordedAt >= startUtc && row.RecordedAt < endUtc
                && row.RecordedAt <= now && (!afterUtc.HasValue || row.RecordedAt > afterUtc.Value))
            .OrderBy(row => row.RecordedAt).ThenBy(row => row.Id).Select(RawProjection)
            .Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        var records = rows.Take(limit).Select(WithUtc).ToArray();
        return new(stationId, startUtc, endUtc, now, records,
            hasMore ? new DateTimeOffset(records[^1].RecordedAtUtc) : null);
    }

    public async Task<V9WeatherHistoryPage<V9WeatherArchiveObservation>> GetArchiveHistoryAsync(string stationId,
        DateTimeOffset start, DateTimeOffset end, int limit = 100, DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        ValidateHistory(stationId, start, end, limit, after);
        cancellationToken.ThrowIfCancellationRequested();
        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;
        var afterUtc = after?.UtcDateTime;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await db.WeatherArchive.AsNoTracking()
            .Where(row => row.StationId == stationId && row.RecordedAtUtc >= startUtc && row.RecordedAtUtc < endUtc
                && row.RecordedAtUtc <= now && (!afterUtc.HasValue || row.RecordedAtUtc > afterUtc.Value))
            .OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.Id)
            .Select(row => new V9WeatherArchiveObservation
            {
                Id = row.Id, StationId = row.StationId, RecordedAtUtc = row.RecordedAtUtc,
                ConsoleRecordedAtLocal = row.ConsoleRecordedAtLocal, ArchiveIntervalMinutes = row.ArchiveIntervalMinutes,
                TemperatureF = row.TemperatureF, HighTemperatureF = row.HighTemperatureF, LowTemperatureF = row.LowTemperatureF,
                HumidityPercent = row.HumidityPercent, BarometricPressureInHg = row.BarometricPressureInHg,
                WindSpeedMph = row.WindSpeedMph, WindGustMph = row.WindGustMph, WindDirectionDegrees = row.WindDirectionDegrees,
                RainfallInches = row.RainfallInches, RainRateInchesPerHour = row.RainRateInchesPerHour,
                SolarRadiationWm2 = row.SolarRadiationWm2, UvIndex = row.UvIndex
            }).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        var records = rows.Take(limit).Select(row => row with
        {
            RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc),
            ConsoleRecordedAtLocal = DateTime.SpecifyKind(row.ConsoleRecordedAtLocal, DateTimeKind.Unspecified)
        }).ToArray();
        return new(stationId, startUtc, endUtc, now, records,
            hasMore ? new DateTimeOffset(records[^1].RecordedAtUtc) : null);
    }

    private async Task<V9WeatherObservation?> LatestAsync(string stationId, DateTime now, CancellationToken ct)
    {
        // Station is validated and the predicate excludes null station IDs. SQL datetime2 loses Kind,
        // so restore the documented UTC storage meaning after materialization without host conversion.
        var row = await db.WeatherRaw.AsNoTracking().Where(row => row.StationId == stationId && row.RecordedAt <= now)
            .OrderByDescending(row => row.RecordedAt).ThenByDescending(row => row.Id)
            .Select(RawProjection).FirstOrDefaultAsync(ct);
        return row is null ? null : WithUtc(row);
    }

    private static V9WeatherObservation WithUtc(V9WeatherObservation row) =>
        row with { RecordedAtUtc = DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc) };

    private static void ValidateStation(string stationId)
    {
        if (string.IsNullOrWhiteSpace(stationId) || stationId.Length > 64 || stationId != stationId.Trim())
            throw new ArgumentException("Station must be a trimmed identifier of 1–64 characters.", nameof(stationId));
    }

    private static void ValidateHistory(string stationId, DateTimeOffset start, DateTimeOffset end, int limit, DateTimeOffset? after)
    {
        ValidateStation(stationId);
        if (end <= start || end - start > MaximumHistoryRange)
            throw new ArgumentOutOfRangeException(nameof(end), "History must have a positive range of at most 31 days.");
        if (limit < 1 || limit > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit), "Page size must be between 1 and 1000.");
        if (after.HasValue && (after.Value < start || after.Value >= end))
            throw new ArgumentOutOfRangeException(nameof(after), "Continuation must be inside the requested range.");
    }
}

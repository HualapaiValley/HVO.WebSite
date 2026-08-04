using System.Text.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Components.Pages;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.Hardware.JkBms.History;

public sealed record BmsHistoryPoint(
    string Address,
    string Alias,
    DateTime RecordedAtUtc,
    double StateOfChargePercent,
    double PackVoltageV,
    double IntoPackCurrentA,
    double IntoPackPowerW,
    double BatteryTemperatureC,
    double CellDeltaMv,
    bool HasAlarm);

public sealed record BmsFleetTrendPoint(
    DateTime RecordedAtUtc,
    double StateOfChargePercent,
    double PackVoltageV,
    double IntoPackCurrentA,
    double IntoPackPowerW,
    double BatteryTemperatureC);

public sealed record BmsDailyHistorySummary(
    DateTime DayUtc,
    double? ChargeEnergyKwh,
    double? DischargeEnergyKwh,
    double? HighVoltageV,
    double? LowVoltageV)
{
    public double? NetEnergyKwh => ChargeEnergyKwh.HasValue && DischargeEnergyKwh.HasValue
        ? ChargeEnergyKwh.Value - DischargeEnergyKwh.Value
        : null;
}

public enum BmsPowerFlowDirection
{
    Unknown,
    Idle,
    Charging,
    Discharging
}

public sealed record BmsHistorySummary(
    double? ChargeEnergyKwh,
    double? DischargeEnergyKwh,
    double? ChargeAmpHours,
    double? DischargeAmpHours,
    double? AverageSocPercent,
    double? SocRatePercentPerHour,
    TimeSpan? TimeToFull,
    TimeSpan? TimeToEmpty)
{
    public BmsPowerFlowDirection PowerDirection { get; init; } = BmsPowerFlowDirection.Unknown;

    public static BmsHistorySummary Empty { get; } = new(null, null, null, null, null, null, null, null);
}

public sealed class BmsHistorySnapshot
{
    public BmsHistorySnapshot(
        IReadOnlyList<BmsHistoryPoint> points,
        DateTime loadedAtUtc,
        DateTime rangeStartUtc,
        TimeZoneInfo? displayTimeZone = null)
    {
        Points = points;
        LoadedAtUtc = loadedAtUtc;
        RangeStartUtc = rangeStartUtc;
        Today = BmsHistoryCalculations.CalculateToday(points, loadedAtUtc, displayTimeZone);
        DailySummaries = BmsHistoryCalculations.CalculateDailySummaries(points, loadedAtUtc, 7, displayTimeZone);
    }

    public IReadOnlyList<BmsHistoryPoint> Points { get; }
    public DateTime LoadedAtUtc { get; }
    public DateTime RangeStartUtc { get; }
    public BmsHistorySummary Today { get; }
    public IReadOnlyList<BmsDailyHistorySummary> DailySummaries { get; }

    public static BmsHistorySnapshot Empty { get; } = new([], DateTime.UtcNow, DateTime.UtcNow);
}

public interface IBmsHistoryService
{
    BmsHistorySnapshot Snapshot { get; }
    Task<BmsHistorySnapshot> RefreshAsync(TimeSpan range, CancellationToken ct = default);
}

public sealed class BmsHistoryService(
    IServiceScopeFactory scopeFactory,
    JkBmsDisplayTimeZoneResolver displayTimeZoneResolver) : IBmsHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private BmsHistorySnapshot _snapshot = BmsHistorySnapshot.Empty;

    public BmsHistorySnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task<BmsHistorySnapshot> RefreshAsync(TimeSpan range, CancellationToken ct = default)
    {
        if (range <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(range));

        var nowUtc = DateTime.UtcNow;
        var current = Snapshot;
        if (nowUtc - current.LoadedAtUtc < CacheLifetime && current.RangeStartUtc <= nowUtc.Subtract(range))
            return current;

        await _refreshLock.WaitAsync(ct);
        try
        {
            nowUtc = DateTime.UtcNow;
            current = Snapshot;
            if (nowUtc - current.LoadedAtUtc < CacheLifetime && current.RangeStartUtc <= nowUtc.Subtract(range))
                return current;

            var rangeStartUtc = nowUtc.Subtract(range);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var records = await db.OutboxRecords
                .AsNoTracking()
                .Where(record => record.PayloadType == BmsOutboxPayloadTypes.Reading
                    && record.Status != EdgeOutboxStatus.Failed
                    && record.RecordedAtUtc >= rangeStartUtc)
                .OrderBy(record => record.RecordedAtUtc)
                .Select(record => record.PayloadJson)
                .ToListAsync(ct);

            var points = new List<BmsHistoryPoint>(records.Count);
            foreach (var payload in records)
            {
                try
                {
                    var ingress = JsonSerializer.Deserialize<BmsIngressRecord>(payload, JsonOptions);
                    if (ingress?.Reading is not null)
                    {
                        var reading = ingress.Reading;
                        points.Add(new BmsHistoryPoint(
                            reading.DeviceAddress,
                            reading.DeviceAlias,
                            reading.RecordedAtUtc,
                            reading.StateOfChargePercent,
                            reading.TotalVoltageMv / 1000d,
                            BmsDisplayFormatting.IntoPackCurrentAmps(reading.CurrentMa),
                            BmsDisplayFormatting.IntoPackPowerWatts(reading.TotalVoltageMv, reading.CurrentMa),
                            reading.BatteryTemperature1C,
                            reading.DeltaCellVoltageMv,
                            reading.HasAlarms));
                    }
                }
                catch (JsonException)
                {
                    // A malformed historical record should not take down the live dashboard.
                }
            }

            var refreshed = new BmsHistorySnapshot(points, nowUtc, rangeStartUtc, displayTimeZoneResolver.TimeZone);
            Volatile.Write(ref _snapshot, refreshed);
            return refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}

public static class BmsHistoryCalculations
{
    private static readonly TimeSpan MaximumIntegrationGap = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RateWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan CurrentPowerWindow = TimeSpan.FromMinutes(15);
    private const double MinimumMeaningfulPowerWatts = 25;

    public static IReadOnlyList<BmsFleetTrendPoint> AggregateFleet(
        IReadOnlyList<BmsHistoryPoint> points,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan bucketSize)
    {
        if (bucketSize <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(bucketSize));

        var result = new List<BmsFleetTrendPoint>();
        for (var bucketStart = startUtc; bucketStart < endUtc; bucketStart = bucketStart.Add(bucketSize))
        {
            var bucketEnd = bucketStart.Add(bucketSize);
            var bucketPoints = points
                .Where(point => point.RecordedAtUtc >= bucketStart && point.RecordedAtUtc < bucketEnd)
                .GroupBy(point => point.Address)
                .Select(group => new
                {
                    StateOfChargePercent = group.Average(point => point.StateOfChargePercent),
                    PackVoltageV = group.Average(point => point.PackVoltageV),
                    IntoPackCurrentA = group.Average(point => point.IntoPackCurrentA),
                    IntoPackPowerW = group.Average(point => point.IntoPackPowerW),
                    BatteryTemperatureC = group.Average(point => point.BatteryTemperatureC)
                })
                .ToArray();

            if (bucketPoints.Length == 0)
                continue;

            result.Add(new BmsFleetTrendPoint(
                bucketStart,
                bucketPoints.Average(point => point.StateOfChargePercent),
                bucketPoints.Average(point => point.PackVoltageV),
                bucketPoints.Sum(point => point.IntoPackCurrentA),
                bucketPoints.Sum(point => point.IntoPackPowerW),
                bucketPoints.Average(point => point.BatteryTemperatureC)));
        }

        return result;
    }

    public static IReadOnlyList<BmsDailyHistorySummary> CalculateDailySummaries(
        IReadOnlyList<BmsHistoryPoint> points,
        DateTime nowUtc,
        int dayCount,
        TimeZoneInfo? displayTimeZone = null)
    {
        if (dayCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(dayCount));

        var timeZone = displayTimeZone ?? TimeZoneInfo.Utc;
        var firstLocalDay = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone).Date.AddDays(-(dayCount - 1));
        return Enumerable.Range(0, dayCount)
            .Select(index => CalculateDailySummary(points, firstLocalDay.AddDays(index), timeZone))
            .Reverse()
            .ToArray();
    }

    private static BmsDailyHistorySummary CalculateDailySummary(
        IReadOnlyList<BmsHistoryPoint> points,
        DateTime localDay,
        TimeZoneInfo displayTimeZone)
    {
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDay, DateTimeKind.Unspecified), displayTimeZone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDay.AddDays(1), DateTimeKind.Unspecified), displayTimeZone);
        var dayPoints = points
            .Where(point => point.RecordedAtUtc >= dayStartUtc && point.RecordedAtUtc < dayEndUtc)
            .ToArray();
        if (dayPoints.Length == 0)
            return new BmsDailyHistorySummary(dayStartUtc, null, null, null, null);

        var chargeWh = 0d;
        var dischargeWh = 0d;
        foreach (var devicePoints in dayPoints.GroupBy(point => point.Address))
        {
            var ordered = devicePoints.OrderBy(point => point.RecordedAtUtc).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                if (current.RecordedAtUtc < dayStartUtc || current.RecordedAtUtc >= dayEndUtc)
                    continue;

                var elapsed = current.RecordedAtUtc - previous.RecordedAtUtc;
                if (elapsed <= TimeSpan.Zero || elapsed > MaximumIntegrationGap)
                    continue;

                var energyWh = Math.Abs(current.IntoPackPowerW) * elapsed.TotalHours;
                if (current.IntoPackPowerW >= 0)
                    chargeWh += energyWh;
                else
                    dischargeWh += energyWh;
            }
        }

        return new BmsDailyHistorySummary(
            dayStartUtc,
            chargeWh / 1000d,
            dischargeWh / 1000d,
            dayPoints.Max(point => point.PackVoltageV),
            dayPoints.Min(point => point.PackVoltageV));
    }

    public static BmsHistorySummary CalculateToday(
        IReadOnlyList<BmsHistoryPoint> points,
        DateTime nowUtc,
        TimeZoneInfo? displayTimeZone = null)
    {
        var timeZone = displayTimeZone ?? TimeZoneInfo.Utc;
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone).Date;
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localToday, DateTimeKind.Unspecified), timeZone);
        var todayEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localToday.AddDays(1), DateTimeKind.Unspecified), timeZone);
        var today = points.Where(point => point.RecordedAtUtc >= todayStartUtc && point.RecordedAtUtc < todayEndUtc).ToArray();
        if (today.Length == 0)
            return BmsHistorySummary.Empty;

        var chargeWh = 0d;
        var dischargeWh = 0d;
        var chargeAh = 0d;
        var dischargeAh = 0d;
        foreach (var devicePoints in today.GroupBy(point => point.Address))
        {
            var ordered = devicePoints.OrderBy(point => point.RecordedAtUtc).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                var elapsed = current.RecordedAtUtc - previous.RecordedAtUtc;
                if (elapsed <= TimeSpan.Zero || elapsed > MaximumIntegrationGap)
                    continue;

                var hours = elapsed.TotalHours;
                if (current.IntoPackPowerW >= 0)
                    chargeWh += current.IntoPackPowerW * hours;
                else
                    dischargeWh += Math.Abs(current.IntoPackPowerW) * hours;

                if (current.IntoPackCurrentA >= 0)
                    chargeAh += current.IntoPackCurrentA * hours;
                else
                    dischargeAh += Math.Abs(current.IntoPackCurrentA) * hours;
            }
        }

        var latestByDevice = today
            .GroupBy(point => point.Address)
            .Select(group => group.OrderByDescending(point => point.RecordedAtUtc).First())
            .ToArray();
        double? averageSoc = latestByDevice.Length == 0 ? null : latestByDevice.Average(point => point.StateOfChargePercent);
        var rate = CalculateSocRate(points, nowUtc);
        var recentPower = CalculateRecentFleetPower(points, nowUtc);
        var powerDirection = recentPower switch
        {
            > MinimumMeaningfulPowerWatts => BmsPowerFlowDirection.Charging,
            < -MinimumMeaningfulPowerWatts => BmsPowerFlowDirection.Discharging,
            not null => BmsPowerFlowDirection.Idle,
            _ => BmsPowerFlowDirection.Unknown
        };

        return new BmsHistorySummary(
            chargeWh / 1000d,
            dischargeWh / 1000d,
            chargeAh,
            dischargeAh,
            averageSoc,
            rate,
            powerDirection == BmsPowerFlowDirection.Charging && rate is > 0.05 && averageSoc.HasValue
                ? TimeSpan.FromHours(Math.Max(0, (100d - averageSoc.Value) / rate.GetValueOrDefault()))
                : null,
            powerDirection == BmsPowerFlowDirection.Discharging && rate is < -0.05 && averageSoc.HasValue
                ? TimeSpan.FromHours(Math.Max(0, averageSoc.Value / Math.Abs(rate.GetValueOrDefault())))
                : null)
        {
            PowerDirection = powerDirection
        };
    }

    public static double? CalculateRecentFleetPower(IReadOnlyList<BmsHistoryPoint> points, DateTime nowUtc)
    {
        var cutoff = nowUtc.Subtract(CurrentPowerWindow);
        var latestByDevice = points
            .Where(point => point.RecordedAtUtc >= cutoff && point.RecordedAtUtc <= nowUtc)
            .GroupBy(point => point.Address)
            .Select(group => group.OrderByDescending(point => point.RecordedAtUtc).First())
            .ToArray();

        return latestByDevice.Length == 0 ? null : latestByDevice.Sum(point => point.IntoPackPowerW);
    }

    public static double? CalculateSocRate(IReadOnlyList<BmsHistoryPoint> points, DateTime nowUtc)
    {
        var cutoff = nowUtc.Subtract(RateWindow);
        var rates = new List<double>();
        foreach (var devicePoints in points.Where(point => point.RecordedAtUtc >= cutoff).GroupBy(point => point.Address))
        {
            var ordered = devicePoints.OrderBy(point => point.RecordedAtUtc).ToArray();
            if (ordered.Length < 2)
                continue;

            var elapsedHours = (ordered[^1].RecordedAtUtc - ordered[0].RecordedAtUtc).TotalHours;
            if (elapsedHours >= 0.1)
                rates.Add((ordered[^1].StateOfChargePercent - ordered[0].StateOfChargePercent) / elapsedHours);
        }

        return rates.Count == 0 ? null : rates.Average();
    }
}
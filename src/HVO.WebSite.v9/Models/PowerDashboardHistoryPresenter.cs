using HVO.WebSite.v9.Configuration;
using HVO.WebSite.Themes.Components.Charts;
using HVO.WebSite.Themes.Components.Format;

namespace HVO.WebSite.v9.Models;

/// <summary>A complete UTC grid for a requested dashboard window; no database, clock or host-local conversion.</summary>
public sealed class PowerDashboardHistoryPresenter
{
    public static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(5);
    public IReadOnlyList<DateTime> TimesUtc { get; }
    public IReadOnlyList<string> Labels { get; }
    public IReadOnlyList<HvoChartDataset> PvDatasets { get; }
    public IReadOnlyList<HvoChartDataset> BatteryDatasets { get; }
    public string DisplayTimeZoneLabel { get; }
    public string Coverage { get; }

    public PowerDashboardHistoryPresenter(PowerTelemetryHistoryResponse history, PowerCompositionOptions compositionOptions,
        DateTime windowStartUtc, DateTime windowEndUtc, HvoDisplayTimeZone displayTimeZone)
    {
        windowStartUtc = AsUtc(windowStartUtc);
        windowEndUtc = AsUtc(windowEndUtc);
        if (windowEndUtc <= windowStartUtc || windowEndUtc - windowStartUtc > TimeSpan.FromHours(48))
            throw new ArgumentOutOfRangeException(nameof(windowEndUtc), "Dashboard history requires a positive window no longer than 48 hours.");
        var first = Bucket(windowStartUtc);
        var last = Bucket(windowEndUtc);
        TimesUtc = Enumerable.Range(0, (int)((last - first).Ticks / BucketSize.Ticks) + 1)
            .Select(index => first.AddTicks(index * BucketSize.Ticks)).ToArray();
        DisplayTimeZoneLabel = displayTimeZone.Label;
        Labels = TimesUtc.Select(time => HvoFormat.Timestamp(time, displayTimeZone.TimeZone,
            windowEndUtc - windowStartUtc > TimeSpan.FromHours(24) ? "MM-dd HH:mm" : "HH:mm")).ToArray();
        var details = history.MpptDetails.Where(detail => InWindow(detail.RecordedAtUtc, windowStartUtc, windowEndUtc)).ToArray();
        var battery = history.BatteryReadings.Where(point => InWindow(point.RecordedAtUtc, windowStartUtc, windowEndUtc)).ToArray();
        PvDatasets = BuildPv(details, compositionOptions);
        BatteryDatasets = BuildBattery(battery);
        var values = PvDatasets.Concat(BatteryDatasets).SelectMany(dataset => dataset.Data).ToArray();
        Coverage = values.All(value => value is null) ? "No observations in this window."
            : values.Any(value => value is null) ? "Partial coverage; gaps indicate missing observations."
            : "Complete observation coverage.";
    }

    private IReadOnlyList<HvoChartDataset> BuildPv(IReadOnlyList<PowerMpptDetailSnapshotResponse> details, PowerCompositionOptions options)
    {
        var expected = options.ExpectedPvTrackerIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (details.Count == 0 || expected.Length == 0) return [];
        var values = details.SelectMany(detail => detail.Trackers.Select(tracker => new PvPoint(
                $"{detail.SourceId}/{tracker.TrackerId}", Bucket(detail.RecordedAtUtc), AsUtc(detail.RecordedAtUtc), detail.Id, tracker.PowerW)))
            .Where(point => expected.Contains(point.TrackerId, StringComparer.OrdinalIgnoreCase))
            .GroupBy(point => point.TrackerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.GroupBy(point => point.Time).ToDictionary(bucket => bucket.Key,
                    bucket => bucket.OrderByDescending(point => point.RecordedAtUtc).ThenByDescending(point => point.Id).First()),
                StringComparer.OrdinalIgnoreCase);
        if (values.Count == 0) return [];
        var palette = new[]
        {
            "#69d3ff", // --hvo-series-1
            "#ffb86c", // --hvo-series-2
            "#ffd166", // --hvo-series-3
        };
        var datasets = expected.Select((trackerId, index) => new HvoChartDataset(trackerId,
            TimesUtc.Select(time => values.TryGetValue(trackerId, out var series) && series.TryGetValue(time, out var point) ? point.PowerW : null).ToArray(),
            BorderColor: palette[index % palette.Length], BorderWidth: 2, PointRadius: 1, Tension: 0.2)).ToList();
        datasets.Add(new("5-minute PV subtotal", TimesUtc.Select(time =>
        {
            var points = expected.Select(trackerId => values.TryGetValue(trackerId, out var series) && series.TryGetValue(time, out var point) ? point : null).ToArray();
            if (points.Any(point => point?.PowerW is null)) return (double?)null;
            var timestamps = points.Select(point => point!.RecordedAtUtc).ToArray();
            return timestamps.Max() - timestamps.Min() <= TimeSpan.FromSeconds(options.MaxDerivationSkewSeconds)
                ? points.Sum(point => point!.PowerW!.Value) : (double?)null;
        }).ToArray(), BorderColor: "#57d38d", // --hvo-series-4
            BorderWidth: 3, PointRadius: 0, Tension: 0.2));
        return datasets;
    }

    private IReadOnlyList<HvoChartDataset> BuildBattery(IReadOnlyList<PowerBatteryHistoryPoint> readings)
    {
        var palette = new[]
        {
            "#57d38d", // --hvo-series-4
            "#6da5ff", // --hvo-accent-blue
        };
        // Retain each device's stream; unrelated devices must never fill each other's gaps.
        return readings.GroupBy(reading => $"{reading.SourceId}\0{reading.DeviceId}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var first = group.First();
                var label = string.IsNullOrWhiteSpace(first.DeviceId) ? first.SourceId : $"{first.SourceId} / {first.DeviceId}";
                var values = group.GroupBy(reading => Bucket(reading.RecordedAtUtc)).ToDictionary(bucket => bucket.Key,
                    bucket => bucket.OrderByDescending(reading => AsUtc(reading.RecordedAtUtc)).First().PowerW);
                return new HvoChartDataset(label, TimesUtc.Select(time => values.TryGetValue(time, out var value) && value.HasValue ? -value.Value : (double?)null).ToArray(),
                    BorderColor: palette[index % palette.Length], BorderWidth: 2, PointRadius: 1, Tension: 0.2);
            }).ToArray();
    }

    private sealed record PvPoint(string TrackerId, DateTime Time, DateTime RecordedAtUtc, long Id, double? PowerW);
    private static bool InWindow(DateTime value, DateTime start, DateTime end) => AsUtc(value) >= start && AsUtc(value) <= end;
    private static DateTime AsUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Local) throw new ArgumentException("History timestamps must be UTC or stored UTC Unspecified values.", nameof(value));
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
    private static DateTime Bucket(DateTime value)
    {
        var utc = AsUtc(value);
        return new(utc.Ticks - utc.Ticks % BucketSize.Ticks, DateTimeKind.Utc);
    }
}

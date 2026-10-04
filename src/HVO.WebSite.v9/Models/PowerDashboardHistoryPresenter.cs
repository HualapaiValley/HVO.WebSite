using HVO.WebSite.v9.Configuration;
using HVO.WebSite.Themes.Components.Charts;

namespace HVO.WebSite.v9.Models;

// Existing history projection is kept separate from refresh; #405 owns its UTC/window refinements.
public sealed class PowerDashboardHistoryPresenter(PowerTelemetryHistoryResponse history, PowerCompositionOptions compositionOptions, int historyHours)
{
    private readonly PowerTelemetryHistoryResponse _history = history;
    private readonly PowerCompositionOptions _compositionOptions = compositionOptions;
    private readonly int _historyHours = historyHours;
    private IReadOnlyList<DateTime> HistoryTimes => _history.MpptDetails.Select(item => Bucket(item.RecordedAtUtc))
        .Concat(_history.BatteryReadings.Select(item => Bucket(item.RecordedAtUtc)))
        .Distinct()
        .OrderBy(value => value)
        .TakeLast(_historyHours * 12 + 1)
        .ToArray();
    public IReadOnlyList<string> Labels => HistoryTimes
        .Select(value => value.ToLocalTime().ToString(_historyHours > 24 ? "MM-dd HH:mm" : "HH:mm"))
        .ToArray();
    public IReadOnlyList<HvoChartDataset> PvDatasets => BuildPvHistoryDatasets();
    public IReadOnlyList<HvoChartDataset> BatteryDatasets => BuildBatteryHistoryDatasets();

    private IReadOnlyList<HvoChartDataset> BuildPvHistoryDatasets()
    {
        var times = HistoryTimes;
        var expected = _compositionOptions.ExpectedPvTrackerIds;
        if (times.Count == 0 || expected.Count == 0)
            return [];
        var values = _history.MpptDetails
            .SelectMany(detail => detail.Trackers.Select(tracker => new
            {
                TrackerId = $"{detail.SourceId}/{tracker.TrackerId}",
                Time = Bucket(detail.RecordedAtUtc),
                detail.RecordedAtUtc,
                tracker.PowerW,
            }))
            .Where(point => expected.Contains(point.TrackerId, StringComparer.OrdinalIgnoreCase))
            .GroupBy(point => point.TrackerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(point => point.Time).ToDictionary(
                    bucket => bucket.Key,
                    bucket => bucket.OrderBy(point => point.RecordedAtUtc).Last()),
                StringComparer.OrdinalIgnoreCase);
        var palette = new[]
        {
            "#69d3ff", // --hvo-series-1
            "#ffb86c", // --hvo-series-2
            "#ffd166", // --hvo-series-3
        };
        var datasets = expected.Select((trackerId, index) => new HvoChartDataset(
            trackerId,
            times.Select(time => values.TryGetValue(trackerId, out var series) && series.TryGetValue(time, out var value) ? value.PowerW : null).ToArray(),
            BorderColor: palette[index % palette.Length],
            BorderWidth: 2,
            PointRadius: 1,
            Tension: 0.2)).ToList();
        datasets.Add(new HvoChartDataset(
            "5-minute PV subtotal",
            times.Select(time =>
            {
                var points = expected.Select(trackerId => values.TryGetValue(trackerId, out var series) && series.TryGetValue(time, out var value) ? value : null).ToArray();
                if (points.Any(point => point?.PowerW is null))
                    return null;
                var timestamps = points.Select(point => point!.RecordedAtUtc.ToUniversalTime()).ToArray();
                return timestamps.Max() - timestamps.Min() <= TimeSpan.FromSeconds(_compositionOptions.MaxDerivationSkewSeconds)
                    ? points.Sum(point => point!.PowerW!.Value)
                    : (double?)null;
            }).ToArray(),
            BorderColor: "#57d38d", // --hvo-series-4
            BorderWidth: 3,
            PointRadius: 0,
            Tension: 0.2));
        return datasets;
    }

    private IReadOnlyList<HvoChartDataset> BuildBatteryHistoryDatasets()
    {
        var times = HistoryTimes;
        var palette = new[]
        {
            "#57d38d", // --hvo-series-4
            "#6da5ff", // --hvo-accent-blue
        };
        return _history.BatteryReadings
            .Where(reading => reading.PowerW.HasValue)
            .GroupBy(reading => reading.SourceId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var values = group.GroupBy(reading => Bucket(reading.RecordedAtUtc))
                    .ToDictionary(bucket => bucket.Key, bucket => bucket.Last().PowerW);
                return new HvoChartDataset(
                    group.Key,
                    times.Select(time => values.TryGetValue(time, out var value) && value.HasValue ? -value.Value : (double?)null).ToArray(),
                    BorderColor: palette[index % palette.Length],
                    BorderWidth: 2,
                    PointRadius: 1,
                    Tension: 0.2);
            })
            .ToArray();
    }

    private static DateTime Bucket(DateTime value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.FromMinutes(5).Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

}

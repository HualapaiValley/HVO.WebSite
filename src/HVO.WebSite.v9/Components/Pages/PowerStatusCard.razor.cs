using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Components;
using HVO.WebSite.Themes.Components.Charts;

namespace HVO.WebSite.v9.Components.Pages;

public partial class PowerStatusCard : ComponentBase
{
    [Inject] private IPowerSystemSnapshotProvider SnapshotProvider { get; set; } = default!;
    [Inject] private IPowerInventoryConfigurationProvider InventoryConfigurationProvider { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;

    private PowerStatusViewModel _viewModel = PowerStatusViewModel.Empty;
    private PowerEg4EquipmentViewModel _eg4Equipment = PowerEg4EquipmentViewModel.Empty;
    private PowerTelemetryHistoryResponse _history = PowerTelemetryHistoryResponse.Empty;
    private PowerCompositionOptions _compositionOptions = new();
    private int _historyHours = 6;

    private string SnapshotStateChipClass => _viewModel.SnapshotState switch
    {
        "Live"    => "hvo-chip-success",
        "Waiting" => "hvo-chip-warning",
        _         => ""
    };

    private static string FreshnessChipClass(string status) => status switch
    {
        "fresh" => "hvo-chip-success",
        "warning" => "hvo-chip-warning",
        "stale" or "invalid" => "hvo-chip-danger",
        _ => "",
    };

    private static string SelectionChipClass(string label)
        => label.StartsWith("Preferred", StringComparison.Ordinal) ? "hvo-chip-success"
            : label.StartsWith("Fallback", StringComparison.Ordinal)
                || label.StartsWith("Selected", StringComparison.Ordinal) ? "hvo-chip-warning"
            : "";

    protected override async Task OnInitializedAsync()
    {
        var snapshot = await SnapshotProvider.GetLatestAsync();
        var compositionOptions = Configuration
            .GetSection(PowerCompositionOptions.SectionName)
            .Get<PowerCompositionOptions>() ?? new PowerCompositionOptions();
        _compositionOptions = compositionOptions;
        _viewModel = PowerStatusViewModel.FromSnapshot(snapshot, compositionOptions);
        var inverterSourceId = Configuration["PowerStatus:Eg4InverterSourceId"] ?? "eg4-6500ex-a";
        var controllerSourceId = Configuration["PowerStatus:Eg4MpptSourceId"] ?? "eg4-mppt100-48hv-a";
        _historyHours = Math.Clamp(Configuration.GetValue("PowerStatus:HistoryHours", 6), 1, 48);
        var eg4Inverter = await InventoryConfigurationProvider.GetLatestInverterDetailAsync(inverterSourceId);
        var eg4Controller = await InventoryConfigurationProvider.GetLatestMpptDetailAsync(controllerSourceId);
        _eg4Equipment = PowerEg4EquipmentViewModel.FromSnapshots(eg4Inverter, eg4Controller);
        _history = await InventoryConfigurationProvider.GetRecentTelemetryAsync(
            compositionOptions.ExpectedPvTrackerIds.Select(TrackerSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            [inverterSourceId, controllerSourceId],
            DateTime.UtcNow.AddHours(-_historyHours));
    }

    private int HistoryRevision => _history.MpptDetails.Count + _history.BatteryReadings.Count;
    private IReadOnlyList<DateTime> HistoryTimes => _history.MpptDetails.Select(item => Bucket(item.RecordedAtUtc))
        .Concat(_history.BatteryReadings.Select(item => Bucket(item.RecordedAtUtc)))
        .Distinct()
        .OrderBy(value => value)
        .TakeLast(_historyHours * 12 + 1)
        .ToArray();
    private IReadOnlyList<string> HistoryLabels => HistoryTimes
        .Select(value => value.ToLocalTime().ToString(_historyHours > 24 ? "MM-dd HH:mm" : "HH:mm"))
        .ToArray();
    private IReadOnlyList<HvoChartDataset> PvHistoryDatasets => BuildPvHistoryDatasets();
    private IReadOnlyList<HvoChartDataset> BatteryHistoryDatasets => BuildBatteryHistoryDatasets();

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

    private static string TrackerSourceId(string trackerId)
    {
        var separator = trackerId.LastIndexOf('/');
        return separator > 0 ? trackerId[..separator] : trackerId;
    }
}

using HVO.Hardware.JkBms.History;
using HVO.Hardware.JkBms.Configuration;
using HVO.WebSite.Themes.Components.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Charts : IDisposable
{
    private static readonly (int Hours, string Label)[] RangeOptions = [(6, "6h"), (24, "24h"), (168, "7d")];
    private static readonly string[] ChartColors =
    [
        "#69d3ff", // --hvo-series-1
        "#ffb86c", // --hvo-series-2
        "#57d38d", // --hvo-accent-success
        "#d6a8ff", // --hvo-series-4
        "#ff8b87", // --hvo-accent-danger
        "#9fd6ff", // --hvo-series-6
        "#ffcf66", // --hvo-accent-amber
    ];

    [Inject] private IBmsHistoryService HistoryService { get; set; } = default!;
    [Inject] private JkBmsDisplayTimeZoneResolver DisplayTimeZoneResolver { get; set; } = default!;
    [Inject] private ILogger<Charts> Logger { get; set; } = default!;

    private BmsHistorySnapshot History { get; set; } = BmsHistorySnapshot.Empty;
    private int _rangeHours = 24;
    private int _revision;
    private CancellationTokenSource? _refreshCts;
    private PeriodicTimer? _refreshTimer;

    private TimeSpan SelectedRange => TimeSpan.FromHours(_rangeHours);
    private string DisplayTimeZoneLabel => DisplayTimeZoneResolver.Label;

    private IReadOnlyList<DateTime> BucketStarts
    {
        get
        {
            var bucketSize = _rangeHours <= 6 ? TimeSpan.FromMinutes(5) : _rangeHours <= 24 ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(2);
            var end = DateTime.UtcNow;
            var start = FloorToBucket(end.Subtract(SelectedRange), bucketSize);
            var buckets = new List<DateTime>();
            for (var bucket = start; bucket <= end; bucket = bucket.Add(bucketSize))
                buckets.Add(bucket);
            return buckets;
        }
    }

    private IReadOnlyList<string> Labels => BucketStarts.Select(bucket =>
        _rangeHours <= 24
            ? DisplayTimeZoneResolver.ConvertFromUtc(bucket).ToString("HH:mm")
            : DisplayTimeZoneResolver.ConvertFromUtc(bucket).ToString("MMM d HH:mm")).ToArray();

    private IReadOnlyList<HvoChartDataset> SocDatasets
    {
        get
        {
            var datasets = new List<HvoChartDataset>
            {
                new("Fleet average", BucketStarts.Select(bucket => Aggregate(bucket, point => point.StateOfChargePercent, false)).ToArray(),
                    BorderColor: "#69d3ff", // --hvo-series-1
                    BackgroundColor: "#69d3ff", // --hvo-series-1
                    BorderWidth: 3,
                    PointRadius: 1,
                    Fill: false)
            };
            foreach (var group in History.Points.GroupBy(point => point.Alias).OrderBy(group => group.Key).Take(7).Select((group, index) => (group, index)))
            {
                datasets.Add(new HvoChartDataset(group.group.Key,
                    BucketStarts.Select(bucket => Aggregate(bucket, point => point.StateOfChargePercent, false, group.group.Key)).ToArray(),
                    BorderColor: ChartColors[group.index],
                    BackgroundColor: ChartColors[group.index],
                    BorderWidth: 1,
                    PointRadius: 0));
            }
            return datasets;
        }
    }

    private IReadOnlyList<HvoChartDataset> PowerDatasets =>
    [
        new("Pack power into (+) / out (-)", BucketStarts.Select(bucket => Aggregate(bucket, point => point.IntoPackPowerW, true)).ToArray(),
            BorderColor: "#ffcf66", // --hvo-accent-amber
            BackgroundColor: "#ffcf66", // --hvo-accent-amber
            BorderWidth: 2,
            PointRadius: 1,
            Fill: true)
    ];

    private IReadOnlyList<HvoChartDataset> TemperatureDatasets =>
    [
        new("Battery temperature", BucketStarts.Select(bucket => Aggregate(bucket, point => point.BatteryTemperatureC, false)).ToArray(),
            BorderColor: "#ff8b87", // --hvo-accent-danger
            BackgroundColor: "#ff8b87", // --hvo-accent-danger
            BorderWidth: 2,
            PointRadius: 1)
    ];

    private IReadOnlyList<HvoChartDataset> DeltaDatasets =>
    [
        new("Maximum cell delta", BucketStarts.Select(bucket => Aggregate(bucket, point => point.CellDeltaMv, true)).ToArray(),
            BorderColor: "#d6a8ff", // --hvo-series-4
            BackgroundColor: "#d6a8ff", // --hvo-series-4
            BorderWidth: 2,
            PointRadius: 1)
    ];

    private IReadOnlyList<string> CurrentSocLabels => History.Points
        .GroupBy(point => point.Address)
        .Select(group => group.OrderByDescending(point => point.RecordedAtUtc).First())
        .OrderBy(point => point.Alias)
        .Select(point => point.Alias)
        .ToArray();

    private IReadOnlyList<HvoChartDataset> CurrentSocDatasets =>
    [
        new("SoC", History.Points
            .GroupBy(point => point.Address)
            .Select(group => group.OrderByDescending(point => point.RecordedAtUtc).First())
            .OrderBy(point => point.Alias)
            .Select(point => (double?)point.StateOfChargePercent)
            .ToArray(),
            BorderColor: "#57d38d", // --hvo-accent-success
            BackgroundColor: "#57d38d", // --hvo-accent-success
            BorderWidth: 1)
    ];

    private IReadOnlyList<string> EnergyLabels => ["Into pack", "Out of pack"];

    private IReadOnlyList<HvoChartDataset> EnergyDatasets =>
    [
        new("Today", [(double?)History.Today.ChargeEnergyKwh ?? 0, (double?)History.Today.DischargeEnergyKwh ?? 0],
            BorderColors: ["#57d38d", "#ff8b87"], // --hvo-accent-success, --hvo-accent-danger
            BackgroundColors: ["#57d38d", "#ff8b87"], // --hvo-accent-success, --hvo-accent-danger
            BorderWidth: 1)
    ];

    private string TrendDescription => History.Today.SocRatePercentPerHour is > 0.05 ? "Charging" : History.Today.SocRatePercentPerHour is < -0.05 ? "Discharging" : "Flat";
    private string ForecastLabel => History.Today.TimeToFull.HasValue ? "to full" : History.Today.TimeToEmpty.HasValue ? "to empty" : "waiting for trend";

    protected override void OnInitialized()
    {
        _refreshCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    public void Dispose()
    {
        _refreshCts?.Cancel();
        var refreshTimer = _refreshTimer;
        _refreshTimer = null;
        refreshTimer?.Dispose();
        _refreshCts?.Dispose();
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        var refreshTimer = _refreshTimer;
        while (refreshTimer is not null && !ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "BMS charts history refresh failed");
            }

            try
            {
                if (!await refreshTimer.WaitForNextTickAsync(ct))
                    break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task SetRangeAsync(int hours)
    {
        _rangeHours = hours;
        await RefreshAsync(CancellationToken.None);
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var historyRange = SelectedRange < TimeSpan.FromDays(1)
            ? TimeSpan.FromDays(1)
            : SelectedRange;
        History = await HistoryService.RefreshAsync(historyRange, ct);
        _revision++;
        await InvokeAsync(StateHasChanged);
    }

    private double? Aggregate(DateTime bucket, Func<BmsHistoryPoint, double> selector, bool sumByDevice, string? alias = null)
    {
        var bucketEnd = bucket.Add(_rangeHours <= 6 ? TimeSpan.FromMinutes(5) : _rangeHours <= 24 ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(2));
        var points = History.Points.Where(point => point.RecordedAtUtc >= bucket && point.RecordedAtUtc < bucketEnd && (alias is null || point.Alias == alias));
        if (sumByDevice)
        {
            var deviceAverages = points.GroupBy(point => point.Address).Select(group => group.Average(selector)).ToArray();
            return deviceAverages.Length == 0 ? null : deviceAverages.Sum();
        }

        var values = points.Select(selector).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private static DateTime FloorToBucket(DateTime value, TimeSpan bucketSize)
        => new(value.Ticks - (value.Ticks % bucketSize.Ticks), DateTimeKind.Utc);

    private static string DisplayKwh(double? value) => value.HasValue ? $"{value.Value:0.00} kWh" : "--";
    private static string DisplayAh(double? value) => value.HasValue ? $"{value.Value:0.0} Ah" : "--";
    private static string DisplayRate(double? value) => value.HasValue ? $"{value.Value:+0.0;-0.0;0.0} %/h" : "--";

    private static string DisplayEta(TimeSpan? value)
    {
        if (!value.HasValue)
            return "--";
        var hours = (int)value.Value.TotalHours;
        return hours >= 24 ? $"{hours / 24}d {hours % 24}h" : $"{hours}h {value.Value.Minutes}m";
    }
}
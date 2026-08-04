using HVO.Hardware.JkBms.Workers;
using HVO.Hardware.JkBms.History;
using HVO.Hardware.JkBms.Configuration;
using HVO.WebSite.Themes.Components.Charts;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Status : IDisposable
{
    [Inject] private ILogger<Status> Logger { get; set; } = default!;
    [Inject] private BmsPollerWorker Poller { get; set; } = default!;
    [Inject] private IBmsHistoryService HistoryService { get; set; } = default!;
    [Inject] private JkBmsDisplayTimeZoneResolver DisplayTimeZoneResolver { get; set; } = default!;

    private IReadOnlyList<DevicePollState> Devices => Poller.DeviceStates;
    private IReadOnlyList<DevicePollState> ReportingDevices => Devices.Where(device => device.LatestReading is not null).ToList();
    private static readonly (int Hours, string Label)[] RangeOptions = [(24, "24h"), (48, "48h"), (168, "7d")];
    private int _chartRevision;
    private int _rangeHours = 24;
    private BmsHistorySnapshot _history = BmsHistorySnapshot.Empty;
    private IReadOnlyList<BmsFleetTrendPoint> _trendPoints = [];
    private DateTime _trendEndUtc = DateTime.UtcNow;
    private CancellationTokenSource? _historyCts;
    private PeriodicTimer? _historyTimer;
    private int TotalBanks => Devices.Count;
    private int ConnectedBanks => Devices.Count(device => device.IsSessionConnected);
    private int ReportingBanks => ReportingDevices.Count;
    private BmsHistorySummary Today => _history.Today;
    private string ForecastLabel => Today.PowerDirection switch
    {
        BmsPowerFlowDirection.Charging => "Charging now",
        BmsPowerFlowDirection.Discharging => "Discharging now",
        BmsPowerFlowDirection.Idle => "No net pack flow",
        _ => "Waiting for recent power"
    };
    private string HoursToFull => Today.TimeToFull.HasValue
        ? DisplayEta(Today.TimeToFull)
        : Today.PowerDirection == BmsPowerFlowDirection.Charging ? "Trend pending" : "Not charging";
    private string HoursToEmpty => Today.TimeToEmpty.HasValue
        ? DisplayEta(Today.TimeToEmpty)
        : Today.PowerDirection == BmsPowerFlowDirection.Discharging ? "Trend pending" : "Not discharging";
    private double? AverageStateOfChargePercent => ReportingDevices.Count > 0 ? ReportingDevices.Average(device => device.LatestReading!.StateOfChargePercent) : null;
    private double? AverageVoltageV => ReportingDevices.Count > 0 ? ReportingDevices.Average(device => device.LatestReading!.TotalVoltageMv / 1000d) : null;
    private double? CurrentIntoA => ReportingDevices.Count > 0 ? ReportingDevices.Sum(device => Math.Max(0, BmsDisplayFormatting.IntoPackCurrentAmps(device.LatestReading!.CurrentMa))) : null;
    private double? CurrentOutA => ReportingDevices.Count > 0 ? ReportingDevices.Sum(device => Math.Max(0, -BmsDisplayFormatting.IntoPackCurrentAmps(device.LatestReading!.CurrentMa))) : null;
    private double? PowerIntoW => ReportingDevices.Count > 0 ? ReportingDevices.Sum(device => Math.Max(0, BmsDisplayFormatting.IntoPackPowerWatts(device.LatestReading!.TotalVoltageMv, device.LatestReading!.CurrentMa))) : null;
    private double? PowerOutW => ReportingDevices.Count > 0 ? ReportingDevices.Sum(device => Math.Max(0, -BmsDisplayFormatting.IntoPackPowerWatts(device.LatestReading!.TotalVoltageMv, device.LatestReading!.CurrentMa))) : null;
    private string DisplayTimeZoneLabel => DisplayTimeZoneResolver.Label;
    private IReadOnlyList<string> TrendLabels => _trendPoints.Select(point => DisplayTimeZoneResolver.ConvertFromUtc(point.RecordedAtUtc).ToString(_rangeHours == 24 ? "HH:mm" : "MMM d HH:mm")).ToArray();
    private TimeSpan TrendBucketSize => _rangeHours switch
    {
        <= 24 => TimeSpan.FromMinutes(15),
        <= 48 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(2)
    };

    protected override void OnInitialized()
    {
        Poller.DeviceStateChanged += OnStateChanged;
        _historyCts = new CancellationTokenSource();
        _historyTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        _ = RefreshHistoryLoopAsync(_historyCts.Token);
        Logger.LogInformation(
            "BMS status page loaded. {DeviceCount} device(s).",
            Poller.DeviceStates.Count);
    }

    public void Dispose()
    {
        Poller.DeviceStateChanged -= OnStateChanged;
        _historyCts?.Cancel();
        _historyTimer?.Dispose();
        _historyCts?.Dispose();
    }

    private async Task RefreshHistoryLoopAsync(CancellationToken ct)
    {
        while (_historyTimer is not null && !ct.IsCancellationRequested)
        {
            try
            {
                await RefreshHistoryAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "BMS history refresh failed");
            }

            try
            {
                if (!await _historyTimer.WaitForNextTickAsync(ct))
                    break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RefreshHistoryAsync(CancellationToken ct)
    {
        _history = await HistoryService.RefreshAsync(TimeSpan.FromDays(7), ct);
        RefreshTrendPoints();
        _chartRevision++;
        await InvokeAsync(StateHasChanged);
    }

    private async void OnStateChanged()
    {
        try
        {
            _chartRevision++;
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "BMS combined dashboard refresh failed");
        }
    }

    private IReadOnlyList<HvoChartDataset> SocDatasets =>
    [
        new HvoChartDataset(
            "State of charge",
            _trendPoints.Select(point => (double?)point.StateOfChargePercent).ToArray(),
            BorderColor: "#57d38d", // --hvo-accent-success
            BackgroundColor: "#57d38d", // --hvo-accent-success
            BorderWidth: 2,
            PointRadius: 1)
    ];

    private IReadOnlyList<HvoChartDataset> PowerDatasets =>
    [
        new HvoChartDataset(
            "Power into pack (+) / out (-)",
            _trendPoints.Select(point => (double?)point.IntoPackPowerW).ToArray(),
            BorderColor: "#ffcf66", // --hvo-accent-amber
            BackgroundColor: "#ffcf66", // --hvo-accent-amber
            BorderWidth: 2,
            PointRadius: 1,
            Fill: true)
    ];

    private IReadOnlyList<HvoChartDataset> VoltageDatasets =>
    [
        new HvoChartDataset(
            "Pack voltage",
            _trendPoints.Select(point => (double?)point.PackVoltageV).ToArray(),
            BorderColor: "#69d3ff", // --hvo-series-1
            BackgroundColor: "#69d3ff", // --hvo-series-1
            BorderWidth: 2,
            PointRadius: 1)
    ];

    private IReadOnlyList<HvoChartDataset> CurrentDatasets =>
    [
        new HvoChartDataset(
            "Current into (+) / out (-)",
            _trendPoints.Select(point => (double?)point.IntoPackCurrentA).ToArray(),
            BorderColor: "#9fd6ff", // --hvo-series-6
            BackgroundColor: "#9fd6ff", // --hvo-series-6
            BorderWidth: 2,
            PointRadius: 1)
    ];

    private IReadOnlyList<HvoChartDataset> TemperatureDatasets =>
    [
        new HvoChartDataset(
            "Battery temperature",
            _trendPoints.Select(point => (double?)point.BatteryTemperatureC).ToArray(),
            BorderColor: "#ff8b87", // --hvo-accent-danger
            BackgroundColor: "#ff8b87", // --hvo-accent-danger
            BorderWidth: 2,
            PointRadius: 1)
    ];

    private async Task SetRangeAsync(int hours)
    {
        if (hours is not (24 or 48 or 168))
            return;

        _rangeHours = hours;
        RefreshTrendPoints();
        _chartRevision++;
        await InvokeAsync(StateHasChanged);
    }

    private void RefreshTrendPoints()
    {
        _trendEndUtc = DateTime.UtcNow;
        _trendPoints = BmsHistoryCalculations.AggregateFleet(
            _history.Points,
            _trendEndUtc.Subtract(TimeSpan.FromHours(_rangeHours)),
            _trendEndUtc,
            TrendBucketSize);
    }

    private static string DisplayCurrent(double? value, bool intoPack = true)
        => value.HasValue ? $"{(intoPack ? "+" : "-")}{value.Value:0.0} A" : "--";

    private static string DisplayPower(double? value, bool intoPack = true)
        => value.HasValue ? $"{(intoPack ? "+" : "-")}{value.Value:0} W" : "--";

    private static string DisplayKwh(double? value)
        => value.HasValue ? $"{value.Value:0.00} kWh" : "--";

    private static string DisplayAh(double? value)
        => value.HasValue ? $"{value.Value:0.0} Ah" : "--";

    private static string DisplaySocRate(double? value)
        => value.HasValue ? $"{value.Value:+0.0;-0.0;0.0} %/h" : "--";

    private static string DisplayEta(TimeSpan? value)
    {
        if (!value.HasValue)
            return "No stable trend";

        var hours = (int)value.Value.TotalHours;
        return hours >= 24
            ? $"{hours / 24}d {hours % 24}h"
            : $"{hours}h {value.Value.Minutes}m";
    }

    private string DisplayDay(DateTime dayUtc) => DisplayTimeZoneResolver.ConvertFromUtc(dayUtc).ToString("ddd, MMM d");

    private static string DisplayCurrent(int? rawCurrentMa)
        => rawCurrentMa.HasValue ? BmsDisplayFormatting.CurrentLabel(rawCurrentMa.Value) : "--";

}

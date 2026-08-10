using HVO.Hardware.Eg4.Dashboard;
using HVO.WebSite.Themes.Components.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.Eg4.Components.Pages;

public partial class Status
{
    [Inject] private IEg4GatewayDashboardState DashboardState { get; set; } = default!;
    [Inject] private ILogger<Status> Logger { get; set; } = default!;
    private Eg4GatewayDashboardSnapshot _snapshot = new([], new Eg4OutboxDashboard(0, 0, null, 0, "Collector not active", null, null, 50, 5, false, false), null);
    private CancellationTokenSource? _refreshCancellation;
    private PeriodicTimer? _refreshTimer;
    private Task? _refreshLoop;
    private int _batchSize = 50;
    private int _sweepIntervalSeconds = 5;
    private bool _outboxDirty;
    private string? _outboxMessage;
    private string ForwardingChipClass => _snapshot.Outbox.FailedCount > 0 ? "hvo-chip-danger" : _snapshot.Outbox.PendingCount > 0 ? "hvo-chip-warning" : string.Empty;
    private string HealthChipClass => _snapshot.HealthState switch
    {
        Eg4DashboardHealthState.Healthy => "hvo-chip-success",
        Eg4DashboardHealthState.Degraded or Eg4DashboardHealthState.Misconfigured => "hvo-chip-warning",
        _ => "hvo-chip-danger",
    };
    private string HealthLabel => $"Gateway {_snapshot.HealthState.ToString().ToLowerInvariant()}";
    private IReadOnlyList<DateTime> HistoryTimes => _snapshot.PowerHistory
        .Select(point => HistoryBucket(point.ObservedAtUtc))
        .Distinct()
        .OrderBy(value => value)
        .TakeLast(180)
        .ToArray();
    private IReadOnlyList<string> HistoryLabels => HistoryTimes
        .Select(value => value.ToLocalTime().ToString("HH:mm"))
        .ToArray();
    private IReadOnlyList<HvoChartDataset> PvDatasets => BuildDatasets(Eg4DashboardSeriesKind.Pv, invertForBatteryView: false);
    private IReadOnlyList<HvoChartDataset> BatteryDatasets => BuildDatasets(Eg4DashboardSeriesKind.Battery, invertForBatteryView: true);

    protected override async Task OnInitializedAsync()
    {
        DashboardState.Changed += HandleDashboardChanged;
        ReadSnapshot(forceFormSync: true);
        _refreshCancellation = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _refreshLoop = RefreshLoopAsync(_refreshCancellation.Token);
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        DashboardState.Changed -= HandleDashboardChanged;
        if (_refreshCancellation is not null) await _refreshCancellation.CancelAsync();
        _refreshTimer?.Dispose();
        if (_refreshLoop is not null)
        {
            try { await _refreshLoop; }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException) { }
        }
        _refreshCancellation?.Dispose();
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        if (_refreshTimer is null) return;
        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await InvokeAsync(() =>
                    {
                        ReadSnapshot();
                        StateHasChanged();
                    });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Logger.LogWarning(exception, "EG4 dashboard view refresh failed; the next scheduled refresh will retry");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void RefreshView()
    {
        ReadSnapshot();
    }

    private void HandleDashboardChanged() => _ = DispatchDashboardChangedAsync();

    private async Task DispatchDashboardChangedAsync()
    {
        try
        {
            await InvokeAsync(() =>
            {
                ReadSnapshot();
                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "EG4 dashboard update could not be dispatched to the circuit");
        }
    }

    private void ReadSnapshot(bool forceFormSync = false)
    {
        _snapshot = DashboardState.GetSnapshot();
        if (!_outboxDirty || forceFormSync)
        {
            _batchSize = _snapshot.Outbox.BatchSize;
            _sweepIntervalSeconds = _snapshot.Outbox.SweepIntervalSeconds;
        }
    }

    private void MarkOutboxDirty() => _outboxDirty = true;

    private void ApplyOutboxSettings()
    {
        try
        {
            var result = DashboardState.UpdateOutboxSettings(new Eg4OutboxSettingsUpdate(_batchSize, _sweepIntervalSeconds));
            _outboxMessage = $"Runtime override applied: batch {result.BatchSize}, sweep {result.SweepIntervalSeconds} seconds.";
            _outboxDirty = false;
            ReadSnapshot(forceFormSync: true);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or InvalidOperationException) { _outboxMessage = exception.Message; }
    }

    private void ResetOutboxSettings()
    {
        try
        {
            var result = DashboardState.UpdateOutboxSettings(new Eg4OutboxSettingsUpdate(Reset: true));
            _batchSize = result.BatchSize;
            _sweepIntervalSeconds = result.SweepIntervalSeconds;
            _outboxDirty = false;
            _outboxMessage = "Runtime settings reset to configured defaults.";
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or InvalidOperationException)
        {
            _outboxMessage = exception.Message;
        }
    }

    private IReadOnlyList<HvoChartDataset> BuildDatasets(Eg4DashboardSeriesKind kind, bool invertForBatteryView)
    {
        var times = HistoryTimes;
        var palette = new[]
        {
            "#69d3ff", // --hvo-series-1
            "#ffb86c", // --hvo-series-2
            "#ffd166", // --hvo-series-3
            "#57d38d", // --hvo-series-4
            "#9fb8d4", // --hvo-series-6
        };
        return _snapshot.PowerHistory
            .Where(point => point.Kind == kind && times.Contains(HistoryBucket(point.ObservedAtUtc)))
            .GroupBy(point => point.SeriesId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var values = group
                    .GroupBy(point => HistoryBucket(point.ObservedAtUtc))
                    .ToDictionary(bucket => bucket.Key, bucket => bucket.Last().PowerW);
                var data = times.Select(time => values.TryGetValue(time, out var value)
                    ? (double?)(invertForBatteryView ? -value : value)
                    : null).ToArray();
                return new HvoChartDataset(
                    group.Last().Label,
                    data,
                    BorderColor: palette[index % palette.Length],
                    BorderWidth: 2,
                    PointRadius: 1,
                    Tension: 0.2);
            })
            .ToArray();
    }

    private static DateTime HistoryBucket(DateTime value)
    {
        var utc = value.ToUniversalTime();
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }
}

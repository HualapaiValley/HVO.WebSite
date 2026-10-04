using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.Themes.Components.Charts;
using Microsoft.AspNetCore.Components;

namespace HVO.WebSite.v9.Components.Pages;

public partial class PowerStatusCard : ComponentBase, IAsyncDisposable
{
    [Inject] private IPowerDashboardQuery Query { get; set; } = default!;
    [Inject] private TimeProvider Clock { get; set; } = default!;
    [Inject] private PowerDashboardSettings Settings { get; set; } = default!;
    [Inject] private ILogger<PowerDashboardSession> Logger { get; set; } = default!;

    private PowerDashboardSession? _session;
    private bool _disposed;
    private bool _isLoading = true;
    private bool _hasLoaded;
    private PowerStatusViewModel _viewModel = PowerStatusViewModel.Empty;
    private PowerEg4EquipmentViewModel _eg4Equipment = PowerEg4EquipmentViewModel.Empty;
    private IReadOnlyDictionary<string, string> _errors = new Dictionary<string, string>();
    private int _historyHours;
    private int HistoryRevision { get; set; }
    private IReadOnlyList<string> HistoryLabels { get; set; } = [];
    private IReadOnlyList<HvoChartDataset> PvHistoryDatasets { get; set; } = [];
    private IReadOnlyList<HvoChartDataset> BatteryHistoryDatasets { get; set; } = [];
    private string HistoryCoverage { get; set; } = "No observations in this window.";

    private string SnapshotStateChipClass => _viewModel.SnapshotState switch
    {
        "Live" => "hvo-chip-success",
        "Waiting" or "Warning" => "hvo-chip-warning",
        "Stale" or "Invalid" => "hvo-chip-danger",
        _ => "",
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
            : label.StartsWith("Fallback", StringComparison.Ordinal) || label.StartsWith("Selected", StringComparison.Ordinal) ? "hvo-chip-warning" : "";

    protected override void OnInitialized()
    {
        _historyHours = Settings.HistoryHours;
        _session = new(Query, Clock, Settings, Logger) { Changed = UpdateAsync };
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // AfterRender does not run during SSR: preserve the loading shell without querying twice.
        if (!firstRender || _session is null) return;
        try { await _session.StartAsync(); }
        catch (OperationCanceledException) when (_disposed) { }
    }

    private async Task UpdateAsync()
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed || _session is null) return;
                _viewModel = _session.ViewModel;
                _eg4Equipment = _session.Equipment;
                _errors = new Dictionary<string, string>(_session.Errors);
                _isLoading = _session.IsLoading;
                _hasLoaded = _session.HasLoaded;
                if (HistoryRevision != _session.HistoryRevision)
                {
                    HistoryRevision = _session.HistoryRevision;
                    var history = new PowerDashboardHistoryPresenter(_session.History, Settings.Composition,
                        _session.HistoryWindowStartUtc, _session.HistoryWindowEndUtc, Settings.DisplayTimeZone);
                    HistoryLabels = history.Labels;
                    PvHistoryDatasets = history.PvDatasets;
                    BatteryHistoryDatasets = history.BatteryDatasets;
                    HistoryCoverage = history.Coverage;
                }
                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            if (!_disposed) await DispatchExceptionAsync(exception);
        }
    }

    private async Task RetryAsync()
    {
        if (_disposed || _session is null) return;
        _isLoading = true;
        try { await _session.RetryAsync(); }
        catch (OperationCanceledException) when (_disposed) { }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_session is not null) await _session.DisposeAsync();
    }
}

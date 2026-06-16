using System.Globalization;

using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Archive : IAsyncDisposable
{
    private const string PageHeadingText = "Archive and history";
    private const string PageSummaryText = "Console archive controls and on-demand historical pulls presented inside the shared Davis shell frame.";

    [Inject] private ILogger<Archive> Logger { get; set; } = default!;

    // ── Archive Interval ─────────────────────────────────────────────────────

    private int _interval = 5;
    private bool _confirmClear;
    private string? _msg;
    private bool _isError;

    protected override void OnInitialized()
    {
        _interval = Math.Max(1, Station.ArchiveIntervalSeconds / 60);
        UpdateShell();
    }

    protected override void OnParametersSet()
    {
        UpdateShell();
    }

    private async Task SaveIntervalAsync()
    {
        Logger.LogInformation("Setting archive interval to {Minutes} minutes", _interval);
        try
        {
            await Station.SetArchiveIntervalAsync(_interval);
            _msg = $"Archive interval set to {_interval} min."; _isError = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set archive interval to {Minutes} minutes", _interval);
            _msg = ex.Message; _isError = true;
        }
        finally
        {
            UpdateShell();
        }
    }

    private async Task ClearAsync()
    {
        _confirmClear = false;
        Logger.LogWarning("User requested archive memory clear");
        try
        {
            await Station.ClearArchiveAsync();
            _msg = "Archive memory cleared."; _isError = false;
            Logger.LogWarning("Archive memory cleared by user");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to clear archive memory");
            _msg = ex.Message; _isError = true;
        }
        finally
        {
            UpdateShell();
        }
    }

    // ── Archive History ──────────────────────────────────────────────────────

    private const int FetchSize = 96;  // records per hardware round-trip (≈2 days at 30-min interval)
    private const int PageSize = 48;   // rows shown per display page (≈1 day at 30-min interval)

    private DateTime _since = DateTime.Now.AddDays(-1);
    private DateTime _historySince;  // snapshot of _since at the moment a load was started
    private bool _historyLoading;
    private string? _historyError;
    private List<ArchiveRecord>? _historyRecords;
    private int _displayPage = 1;
    private DateTime? _continuationTimestamp;  // start of next hardware fetch
    private bool _hasMore;
    private CancellationTokenSource? _loadCts;

    private int TotalPages => _historyRecords is null ? 0
        : Math.Max(1, (int)Math.Ceiling(_historyRecords.Count / (double)PageSize));

    private IEnumerable<ArchiveRecord> PagedRecords =>
        _historyRecords?.Skip((_displayPage - 1) * PageSize).Take(PageSize)
        ?? [];

    private void OnSinceChanged(ChangeEventArgs e)
    {
        if (DateTime.TryParseExact(
            e.Value?.ToString(),
            ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var since))
        {
            _since = since;
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (_historyLoading) return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _historySince = _since;
        _historyLoading = true;
        _historyError = null;
        _historyRecords = null;
        _displayPage = 1;
        _continuationTimestamp = null;
        _hasMore = false;
        StateHasChanged();
        Logger.LogDebug("Loading archive history since {Since}", _since);
        try
        {
            var records = new List<ArchiveRecord>();
            int batchCount = 0;
            await foreach (var rec in Station.GetArchiveSinceAsync(_since, FetchSize, fallbackOnEmpty: true).WithCancellation(ct))
            {
                records.Add(rec);
                if (++batchCount % 5 == 0)
                {
                    _historyRecords = [.. records];
                    StateHasChanged();
                }
            }
            _historyRecords = records;
            if (records.Count >= FetchSize)
            {
                _hasMore = true;
                // AddMinutes(1) steps past the last record so the next DMPAFT request
                // (which returns records >= the given timestamp) does not re-fetch it.
                _continuationTimestamp = records[^1].DateTimeLocal.AddMinutes(1);
            }
            Logger.LogInformation("Archive history loaded: {Count} records since {Since}",
                records.Count, _historySince);
        }
        catch (OperationCanceledException)
        {
            _historyError = "Load cancelled.";
            Logger.LogDebug("Archive history load cancelled");
        }
        catch (Exception ex)
        {
            _historyError = ex.Message;
            Logger.LogError(ex, "Failed to load archive history since {Since}", _historySince);
        }
        finally
        {
            _historyLoading = false;
            UpdateShell();
        }
    }

    private async Task LoadMoreAsync()
    {
        if (_continuationTimestamp is null || _historyLoading) return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _historyLoading = true;
        _hasMore = false;
        StateHasChanged();
        Logger.LogDebug("Loading more archive records from {Ts}", _continuationTimestamp);
        try
        {
            var records = _historyRecords ?? [];
            int batchCount = 0;
            await foreach (var rec in Station.GetArchiveSinceAsync(_continuationTimestamp.Value, FetchSize, fallbackOnEmpty: true).WithCancellation(ct))
            {
                records.Add(rec);
                if (++batchCount % 5 == 0)
                {
                    _historyRecords = [.. records];
                    StateHasChanged();
                }
            }
            _historyRecords = records;
            if (batchCount >= FetchSize)
            {
                _hasMore = true;
                _continuationTimestamp = records[^1].DateTimeLocal.AddMinutes(1);
            }
            else
            {
                _continuationTimestamp = null;
            }
            Logger.LogDebug("{N} more archive records loaded", batchCount);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("LoadMore archive history cancelled");
        }
        catch (Exception ex)
        {
            _historyError = ex.Message;
            Logger.LogWarning(ex, "Failed to load more archive records from {Ts}", _continuationTimestamp);
        }
        finally
        {
            _historyLoading = false;
            UpdateShell();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_loadCts is not null)
        {
            await _loadCts.CancelAsync();
            _loadCts.Dispose();
        }
    }

    internal static string Fmt(double? v, string fmt = "F1") =>
        v.HasValue ? v.Value.ToString(fmt) : "—";

    private void UpdateShell()
    {
    }
}

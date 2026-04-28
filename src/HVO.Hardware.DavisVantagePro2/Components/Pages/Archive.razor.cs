using HVO.Hardware.DavisVantagePro2.Protocol.Packets;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Archive
{
    // ── Archive Interval ─────────────────────────────────────────────────────

    private int _interval = 5;
    private bool _confirmClear;
    private string? _msg;
    private bool _isError;

    private async Task SaveIntervalAsync()
    {
        try
        {
            await Station.SetArchiveIntervalAsync(_interval);
            _msg = $"Archive interval set to {_interval} min."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }

    private async Task ClearAsync()
    {
        _confirmClear = false;
        try
        {
            await Station.ClearArchiveAsync();
            _msg = "Archive memory cleared."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }

    // ── Archive History ──────────────────────────────────────────────────────

    private const int FetchSize = 96;  // records per hardware round-trip (≈2 days at 30-min interval)
    private const int PageSize = 48;   // rows shown per display page (≈1 day at 30-min interval)

    private DateTime _since = DateTime.Now.AddDays(-1);
    private bool _historyLoading;
    private string? _historyError;
    private List<ArchiveRecord>? _historyRecords;
    private int _displayPage = 1;
    private DateTime? _continuationTimestamp;  // start of next hardware fetch
    private bool _hasMore;

    private int TotalPages => _historyRecords is null ? 0
        : Math.Max(1, (int)Math.Ceiling(_historyRecords.Count / (double)PageSize));

    private IEnumerable<ArchiveRecord> PagedRecords =>
        _historyRecords?.Skip((_displayPage - 1) * PageSize).Take(PageSize)
        ?? [];

    private async Task LoadHistoryAsync()
    {
        _historyLoading = true;
        _historyError = null;
        _historyRecords = null;
        _displayPage = 1;
        _continuationTimestamp = null;
        _hasMore = false;
        StateHasChanged();
        try
        {
            var records = new List<ArchiveRecord>();
            int batchCount = 0;
            await foreach (var rec in Station.GetArchiveSinceAsync(_since, FetchSize, fallbackOnEmpty: true))
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
        }
        catch (Exception ex)
        {
            _historyError = ex.Message;
        }
        finally
        {
            _historyLoading = false;
        }
    }

    private async Task LoadMoreAsync()
    {
        if (_continuationTimestamp is null || _historyLoading) return;
        _historyLoading = true;
        _hasMore = false;
        StateHasChanged();
        try
        {
            var records = _historyRecords ?? [];
            int batchCount = 0;
            await foreach (var rec in Station.GetArchiveSinceAsync(_continuationTimestamp.Value, FetchSize, fallbackOnEmpty: true))
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
        }
        catch (Exception ex)
        {
            _historyError = ex.Message;
        }
        finally
        {
            _historyLoading = false;
        }
    }
}

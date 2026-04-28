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

    private DateTime _since = DateTime.Now.AddDays(-1);
    private bool _historyLoading;
    private string? _historyError;
    private List<ArchiveRecord>? _historyRecords;

    private async Task LoadHistoryAsync()
    {
        _historyLoading = true;
        _historyError = null;
        _historyRecords = null;
        StateHasChanged();
        try
        {
            var records = new List<ArchiveRecord>();
            await foreach (var rec in Station.GetArchiveSinceAsync(_since))
                records.Add(rec);
            _historyRecords = records;
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

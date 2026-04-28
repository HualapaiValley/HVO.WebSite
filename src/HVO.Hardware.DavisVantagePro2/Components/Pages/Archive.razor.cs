namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Archive
{
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
}

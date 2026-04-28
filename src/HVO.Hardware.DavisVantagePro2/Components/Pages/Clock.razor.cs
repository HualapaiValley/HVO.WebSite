namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Clock
{
    private DateTime _consoleTime;
    private DateTime _systemTime;
    private double _drift;
    private double _absDrift;
    private bool _loading;
    private bool _syncing;
    private string? _error;
    private string? _msg;
    private bool _isError;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try
        {
            _consoleTime = await Station.GetConsoleTimeAsync();
            _systemTime  = DateTime.Now;
            _drift       = (_systemTime - _consoleTime).TotalSeconds;
            _absDrift    = Math.Abs(_drift);
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task SyncAsync()
    {
        _syncing = true; _msg = null;
        try
        {
            await Station.SetConsoleTimeAsync();
            _msg = "Console clock synced to system time."; _isError = false;
            await LoadAsync();
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
        finally { _syncing = false; }
    }
}

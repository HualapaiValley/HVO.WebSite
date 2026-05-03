using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Clock
{
    [Inject] private ILogger<Clock> Logger { get; set; } = default!;

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
        Logger.LogDebug("Loading console clock");
        try
        {
            _consoleTime = await Station.GetConsoleTimeAsync();
            _systemTime = DateTime.Now;
            _drift = (_systemTime - _consoleTime).TotalSeconds;
            _absDrift = Math.Abs(_drift);
            if (_absDrift > 60)
                Logger.LogWarning("Console clock drift is {Drift:F1}s — sync recommended", _drift);
            else
                Logger.LogDebug("Console clock loaded. Drift: {Drift:F1}s", _drift);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to read console clock");
            _error = ex.Message;
        }
        finally { _loading = false; }
    }

    private async Task SyncAsync()
    {
        _syncing = true; _msg = null;
        Logger.LogInformation("Syncing console clock to system time (current drift: {Drift:F1}s)", _drift);
        try
        {
            await Station.SetConsoleTimeAsync();
            _msg = "Console clock synced to system time."; _isError = false;
            Logger.LogInformation("Console clock synced successfully");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to sync console clock");
            _msg = ex.Message; _isError = true;
        }
        finally { _syncing = false; }
    }
}

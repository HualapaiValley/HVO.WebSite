using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Barometer
{
    [Inject] private ILogger<Barometer> Logger { get; set; } = default!;

    private BarometerData? _bar;
    private bool _loading;
    private string? _loadError;
    private string? _msg;
    private bool _isError;
    private double _setPressure = 29.92;
    private double _setAlt;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _loadError = null;
        Logger.LogDebug("Loading barometer data");
        try { _bar = await Station.GetBarometerDataAsync(); _setAlt = _bar.AltitudeFeet; }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load barometer data");
            _loadError = ex.Message;
        }
        finally { _loading = false; }
    }

    private async Task SaveAsync()
    {
        Logger.LogInformation("Setting barometer: pressure={Pressure:F2} inHg, altitude={Altitude:F0} ft",
            _setPressure, _setAlt);
        try
        {
            await Station.SetBarometerAsync(_setPressure, _setAlt);
            await LoadAsync();
            _msg = "Barometer calibration saved."; _isError = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set barometer: pressure={Pressure:F2} inHg, altitude={Altitude:F0} ft",
                _setPressure, _setAlt);
            _msg = ex.Message; _isError = true;
        }
    }
}

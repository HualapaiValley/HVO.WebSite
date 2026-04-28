using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Barometer
{
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
        try { _bar = await Station.GetBarometerDataAsync(); _setAlt = _bar.AltitudeFeet; }
        catch (Exception ex) { _loadError = ex.Message; }
        finally { _loading = false; }
    }

    private async Task SaveAsync()
    {
        try
        {
            await Station.SetBarometerAsync(_setPressure, _setAlt);
            await LoadAsync();
            _msg = "Barometer calibration saved."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }
}

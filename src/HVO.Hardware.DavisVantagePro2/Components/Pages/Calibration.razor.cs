using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Calibration
{
    [Inject] private ILogger<Calibration> Logger { get; set; } = default!;

    private CalibrationData? _cal;
    private bool _loading;
    private string? _loadError;
    private string? _msg;
    private bool _isError;

    private double _inTempOff, _outTempOff;
    private int _inHumOff, _outHumOff, _windDirOff;

    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        Logger.LogDebug("Loading calibration data");
        try
        {
            _cal = await Station.GetCalibrationAsync();
            _inTempOff = _cal.InsideTempOffsetF;
            _outTempOff = _cal.OutsideTempOffsetF;
            _inHumOff = (int)_cal.InsideHumidOffsetPct;
            _outHumOff = (int)_cal.OutsideHumidOffsetPct;
            _windDirOff = _cal.WindDirOffsetDegrees;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load calibration data");
            _loadError = ex.Message;
        }
        finally { _loading = false; }
    }

    private async Task SaveTempAsync(string variable, double offset)
    {
        Logger.LogInformation("Setting {Variable} temperature offset to {Offset:F1}°F", variable, offset);
        try
        {
            await Station.SetCalibrationTempAsync(variable, offset);
            _msg = $"{variable} offset saved."; _isError = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set {Variable} temperature offset to {Offset:F1}°F", variable, offset);
            _msg = ex.Message; _isError = true;
        }
    }

    private async Task SaveHumAsync(string variable, int offset)
    {
        Logger.LogInformation("Setting {Variable} humidity offset to {Offset}%", variable, offset);
        try
        {
            await Station.SetCalibrationHumidityAsync(variable, offset);
            _msg = $"{variable} offset saved."; _isError = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set {Variable} humidity offset to {Offset}%", variable, offset);
            _msg = ex.Message; _isError = true;
        }
    }

    private async Task SaveWindAsync()
    {
        Logger.LogInformation("Setting wind direction offset to {Offset}°", _windDirOff);
        try
        {
            await Station.SetCalibrationWindDirAsync(_windDirOff);
            _msg = "Wind direction offset saved."; _isError = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set wind direction offset to {Offset}°", _windDirOff);
            _msg = ex.Message; _isError = true;
        }
    }
}

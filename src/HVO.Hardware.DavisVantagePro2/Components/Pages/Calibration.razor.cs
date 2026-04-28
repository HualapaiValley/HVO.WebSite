using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Calibration
{
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
        try
        {
            _cal = await Station.GetCalibrationAsync();
            _inTempOff  = _cal.InsideTempOffsetF;
            _outTempOff = _cal.OutsideTempOffsetF;
            _inHumOff   = (int)_cal.InsideHumidOffsetPct;
            _outHumOff  = (int)_cal.OutsideHumidOffsetPct;
            _windDirOff = _cal.WindDirOffsetDegrees;
        }
        catch (Exception ex) { _loadError = ex.Message; }
        finally { _loading = false; }
    }

    private async Task SaveTempAsync(string variable, double offset)
    {
        try
        {
            await Station.SetCalibrationTempAsync(variable, offset);
            _msg = $"{variable} offset saved."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }

    private async Task SaveHumAsync(string variable, int offset)
    {
        try
        {
            await Station.SetCalibrationHumidityAsync(variable, offset);
            _msg = $"{variable} offset saved."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }

    private async Task SaveWindAsync()
    {
        try
        {
            await Station.SetCalibrationWindDirAsync(_windDirOff);
            _msg = "Wind direction offset saved."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }
}

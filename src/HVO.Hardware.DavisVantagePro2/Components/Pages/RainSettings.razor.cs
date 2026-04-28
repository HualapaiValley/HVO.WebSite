using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class RainSettings
{
    private StationSettings? _settings;
    private bool _loading;
    private string? _loadError;
    private string? _msg;
    private bool _isError;
    private int _bucketType;
    private int _rainYearStart = 1;

    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        try
        {
            _settings = await Station.GetStationSettingsAsync();
            _bucketType    = _settings.RainBucketType;
            _rainYearStart = _settings.RainYearStartMonth;
        }
        catch (Exception ex) { _loadError = ex.Message; }
        finally { _loading = false; }
    }

    private async Task SaveBucketAsync()
    {
        try
        {
            await Station.SetRainBucketTypeAsync(_bucketType);
            _msg = "Rain bucket type saved."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }

    private async Task SaveRainYearAsync()
    {
        try
        {
            await Station.SetRainYearStartAsync(_rainYearStart);
            _msg = "Rain year start saved."; _isError = false;
        }
        catch (Exception ex) { _msg = ex.Message; _isError = true; }
    }
}

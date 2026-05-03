using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class RainSettings
{
    [Inject] private ILogger<RainSettings> Logger { get; set; } = default!;

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
        Logger.LogDebug("Loading rain settings");
        try
        {
            _settings = await Station.GetStationSettingsAsync();
            _bucketType = _settings.RainBucketType;
            _rainYearStart = _settings.RainYearStartMonth;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load rain settings");
            _loadError = ex.Message;
        }
        finally { _loading = false; }
    }

    private async Task SaveBucketAsync()
    {
        Logger.LogInformation("Setting rain bucket type to {Type}", _bucketType);
        try
        {
            await Station.SetRainBucketTypeAsync(_bucketType);
            _msg = "Rain bucket type saved."; _isError = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set rain bucket type to {Type}", _bucketType);
            _msg = ex.Message; _isError = true;
        }
    }

    private async Task SaveRainYearAsync()
    {
        Logger.LogInformation("Setting rain year start to month {Month}", _rainYearStart);
        try
        {
            await Station.SetRainYearStartAsync(_rainYearStart);
            _msg = "Rain year start saved."; _isError = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set rain year start to month {Month}", _rainYearStart);
            _msg = ex.Message; _isError = true;
        }
    }
}

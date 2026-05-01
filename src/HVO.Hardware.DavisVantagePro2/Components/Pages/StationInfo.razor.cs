using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using StationInfoModel = HVO.Hardware.DavisVantagePro2.Station.Models.StationInfo;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class StationInfo
{
    [Inject] private ILogger<StationInfo> Logger { get; set; } = default!;

    private StationInfoModel? _info;
    private bool _loading;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        Logger.LogDebug("Loading station info");
        try { _info = await Station.GetStationInfoAsync(); }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load station info");
            _error = ex.Message;
        }
        finally { _loading = false; }
    }
}

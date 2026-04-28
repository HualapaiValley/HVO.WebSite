using StationInfoModel = HVO.Hardware.DavisVantagePro2.Station.Models.StationInfo;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class StationInfo
{
    private StationInfoModel? _info;
    private bool _loading;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try { _info = await Station.GetStationInfoAsync(); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }
}

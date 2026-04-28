using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Settings
{
    private StationSettings? _s;
    private bool _loading;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try { _s = await Station.GetStationSettingsAsync(); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private static string RainBucketLabel(int t) => t switch
    {
        0 => "0.01 in",
        1 => "0.2 mm",
        2 => "0.1 mm",
        _ => t.ToString()
    };
}

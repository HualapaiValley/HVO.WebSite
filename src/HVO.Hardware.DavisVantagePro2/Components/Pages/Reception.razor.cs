using HVO.Hardware.DavisVantagePro2.Station.Models;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Reception
{
    private ReceptionStats? _stats;
    private bool _loading;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try { _stats = await Station.GetReceptionStatsAsync(); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }
}

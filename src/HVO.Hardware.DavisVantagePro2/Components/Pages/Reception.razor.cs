using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Reception
{
    [Inject] private ILogger<Reception> Logger { get; set; } = default!;

    private ReceptionStats? _stats;
    private bool _loading;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        Logger.LogDebug("Loading reception statistics");
        try { _stats = await Station.GetReceptionStatsAsync(); }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load reception statistics");
            _error = ex.Message;
        }
        finally { _loading = false; }
    }
}

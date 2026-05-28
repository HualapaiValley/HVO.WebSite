using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Components;

namespace HVO.WebSite.v9.Components.Pages;

public partial class PowerStatusCard : ComponentBase
{
    [Inject] private IPowerSystemSnapshotProvider SnapshotProvider { get; set; } = default!;
    [Inject] private IPowerInventoryConfigurationProvider InventoryConfigurationProvider { get; set; } = default!;

    private PowerStatusViewModel _viewModel = PowerStatusViewModel.Empty;
    private PowerInventoryConfigurationViewModel _inventoryConfiguration = PowerInventoryConfigurationViewModel.Empty;

    protected override async Task OnInitializedAsync()
    {
        var snapshot = await SnapshotProvider.GetLatestAsync();
        _viewModel = PowerStatusViewModel.FromSnapshot(snapshot);
        var (inventory, configuration) = await InventoryConfigurationProvider.GetLatestAsync();
        _inventoryConfiguration = PowerInventoryConfigurationViewModel.FromSnapshots(inventory, configuration);
    }
}

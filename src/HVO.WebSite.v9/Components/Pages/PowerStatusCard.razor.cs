using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Components;

namespace HVO.WebSite.v9.Components.Pages;

public partial class PowerStatusCard : ComponentBase
{
    [Inject] private IPowerSystemSnapshotProvider SnapshotProvider { get; set; } = default!;
    [Inject] private IPowerInventoryConfigurationProvider InventoryConfigurationProvider { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;

    private PowerStatusViewModel _viewModel = PowerStatusViewModel.Empty;
    private PowerInventoryConfigurationViewModel _inventoryConfiguration = PowerInventoryConfigurationViewModel.Empty;
    private PowerGatewayStatusViewModel _gatewayStatus = PowerGatewayStatusViewModel.Empty;
    private PowerSolarAssistantDetailViewModel _solarAssistantDetail = PowerSolarAssistantDetailViewModel.Empty;

    private string SnapshotStateChipClass => _viewModel.SnapshotState switch
    {
        "Live"    => "hvo-chip-success",
        "Waiting" => "hvo-chip-warning",
        _         => ""
    };

    private static string FreshnessChipClass(string status) => status switch
    {
        "fresh" => "hvo-chip-success",
        "warning" => "hvo-chip-warning",
        "stale" or "invalid" => "hvo-chip-danger",
        _ => "",
    };

    private static string SelectionChipClass(string label)
        => label.StartsWith("Preferred", StringComparison.Ordinal) ? "hvo-chip-success"
            : label.StartsWith("Fallback", StringComparison.Ordinal)
                || label.StartsWith("Selected", StringComparison.Ordinal) ? "hvo-chip-warning"
            : "";

    protected override async Task OnInitializedAsync()
    {
        var snapshot = await SnapshotProvider.GetLatestAsync();
        var compositionOptions = Configuration
            .GetSection(PowerCompositionOptions.SectionName)
            .Get<PowerCompositionOptions>() ?? new PowerCompositionOptions();
        _viewModel = PowerStatusViewModel.FromSnapshot(snapshot, compositionOptions);
        var (inventory, configuration, energy, inverterDetail, gatewayStatus) = await InventoryConfigurationProvider.GetLatestCentralAsync();
        _inventoryConfiguration = PowerInventoryConfigurationViewModel.FromSnapshots(inventory, configuration);
        _gatewayStatus = PowerGatewayStatusViewModel.FromSnapshot(gatewayStatus);
        _solarAssistantDetail = PowerSolarAssistantDetailViewModel.FromSnapshots(
            energy,
            inverterDetail,
            Configuration["PowerStatus:SolarAssistantGatewayUrl"]);
    }
}

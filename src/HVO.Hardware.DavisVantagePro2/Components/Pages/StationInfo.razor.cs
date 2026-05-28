using HVO.Hardware.DavisVantagePro2.Components.Layout;
using HVO.Hardware.DavisVantagePro2.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Globalization;
using StationInfoModel = HVO.Hardware.DavisVantagePro2.Station.Models.StationInfo;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class StationInfo : IDisposable
{
    private const string PageHeadingText = "Station info";
    private const string PageSummaryText = "Read-only console identity, firmware, and clock status presented inside the shared Davis shell frame.";

    [Inject] private ILogger<StationInfo> Logger { get; set; } = default!;
    [Inject] private DavisSiteState SiteState { get; set; } = default!;
    [CascadingParameter] private ShellLayoutState? ShellLayoutState { get; set; }

    private StationInfoModel? Info => SiteState.StationInfo;

    private string? ErrorMessage => SiteState.InitializationError;

    private string HardwareDescriptionText => Info?.HardwareDescription ?? "Loading console identity";

    private string ModelTypeText => Info?.ModelType.ToString(CultureInfo.InvariantCulture) ?? "-";

    private string FirmwareVersionText => Info?.FirmwareVersion ?? "-";

    private string FirmwareDateText => Info?.FirmwareDate ?? "-";

    private string ConsoleTimeText => Info?.ConsoleTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Waiting for console time";

    private string DriftText => Info is null
        ? "Waiting for console time"
        : $"{Math.Abs((DateTime.Now - Info.ConsoleTime).TotalSeconds):F1} s";

    private string SnapshotStatusText => SiteState.StationInfoSavedAtUtc.HasValue
        ? $"Snapshot cached {SiteState.StationInfoSavedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
        : "No cached station snapshot";

    protected override void OnInitialized()
    {
        SiteState.Changed += HandleSiteStateChanged;
        UpdateShell();
        _ = SiteState.EnsureInitializedAsync();
    }

    protected override void OnParametersSet()
    {
        UpdateShell();
    }

    private async Task LoadAsync()
    {
        Logger.LogDebug("Refreshing shared station info snapshot");
        await SiteState.RefreshStationInfoAsync();
    }

    public void Dispose()
    {
        SiteState.Changed -= HandleSiteStateChanged;
    }

    private void UpdateShell()
    {
        ShellLayoutState?.SetPage("Station Info", PageHeadingText, PageSummaryText);
    }

    private void HandleSiteStateChanged()
    {
        _ = InvokeAsync(() =>
        {
            UpdateShell();
            StateHasChanged();
        });
    }
}

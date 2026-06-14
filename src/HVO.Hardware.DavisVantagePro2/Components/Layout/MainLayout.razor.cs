using System.Globalization;
using HVO.Hardware.DavisVantagePro2.Services;
using HVO.WebSite.Themes.Components.Format;
using HVO.WebSite.Themes.Components.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.Hardware.DavisVantagePro2.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private const string StationLocationText = "Hualapai Valley, AZ";
    private static readonly TimeSpan LiveLoopFreshnessThreshold = TimeSpan.FromSeconds(10);

    private readonly ShellLayoutState _shellState = new();
    private bool _showStationInfoDialog;

    [Inject] private DavisSiteState SiteState { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private ShellFooterItem _footer1 = new("Davis VP2");
    private ShellFooterItem _footer2 = new("Weather overview");
    private ShellFooterItem _footer3 = new("MudBlazor shell");
    private ShellFooterItem _footer4 = new("Fixed width layout");
    private ShellFooterItem _footer5 = new("Connection");

    private string ThemeSelectorIcon => _shellState.IsDarkMode
        ? Icons.Material.Outlined.DarkMode
        : Icons.Material.Outlined.LightMode;

    private string ThemeSelectorLabel => _shellState.IsDarkMode ? "Switch to light theme" : "Switch to dark theme";

    private string StationHardwareDescriptionText => SiteState.StationInfo?.HardwareDescription ?? "Loading console identity";

    private string StationHardwareTypeText => SiteState.StationInfo?.HardwareType.ToString(CultureInfo.InvariantCulture) ?? "-";

    private string StationModelTypeText => SiteState.StationInfo?.ModelType.ToString(CultureInfo.InvariantCulture) ?? "-";

    private string StationFirmwareVersionText => SiteState.StationInfo?.FirmwareVersion ?? "-";

    private string StationFirmwareDateText => SiteState.StationInfo?.FirmwareDate ?? "-";

    private string StationConsoleTimeText => SiteState.StationInfo?.ConsoleTime is { } ct
        ? HvoFormat.Timestamp(ct, "yyyy-MM-dd HH:mm:ss")
        : "Waiting for console time";

    private string StationSnapshotStatusText => SiteState.StationInfoSavedAtUtc.HasValue
        ? $"Cached {HvoFormat.Timestamp(SiteState.StationInfoSavedAtUtc, "yyyy-MM-dd HH:mm:ss")}"
        : "No cached station snapshot";

    protected override void OnInitialized()
    {
        _shellState.Changed += HandleShellStateChanged;
        SiteState.Changed += HandleSiteStateChanged;
        UpdateSiteFooter();
        _ = SiteState.EnsureInitializedAsync();
    }

    public void Dispose()
    {
        _shellState.Changed -= HandleShellStateChanged;
        SiteState.Changed -= HandleSiteStateChanged;
    }

    private void OnThemeModeChanged(bool useDarkMode)
    {
        _shellState.SetTheme(useDarkMode);
    }

    private void ToggleTheme()
    {
        _shellState.ToggleTheme();
    }

    private void OpenStationInfoDialog() => _showStationInfoDialog = true;
    private void CloseStationInfoDialog() => _showStationInfoDialog = false;

    private Variant GetNavLinkVariant(string section)
        => IsCurrentSection(section) ? Variant.Filled : Variant.Text;

    private string GetNavLinkClass(string section, string? additionalClass = null)
    {
        var baseClass = IsCurrentSection(section)
            ? "shell-nav-link shell-nav-link-current"
            : "shell-nav-link";

        return string.IsNullOrWhiteSpace(additionalClass)
            ? baseClass
            : $"{baseClass} {additionalClass}";
    }

    private bool IsCurrentSection(string section)
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
        return section switch
        {
            "Overview" => path is "" or "monitor",
            "Alerts" => path.StartsWith("alarm", StringComparison.OrdinalIgnoreCase),
            "Archive" => path.StartsWith("archive", StringComparison.OrdinalIgnoreCase),
            "Configuration" => path.StartsWith("console-settings", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private async Task RetryInitializationAsync() => await SiteState.RetryInitializationAsync();

    private void HandleShellStateChanged() => _ = InvokeAsync(StateHasChanged);

    private void HandleSiteStateChanged()
    {
        _ = InvokeAsync(() =>
        {
            UpdateSiteFooter();
            StateHasChanged();
        });
    }

    private void UpdateSiteFooter()
    {
        _footer1 = BuildLiveFooterItem();
        _footer2 = new ShellFooterItem(SiteState.StationIdentityText);
        _footer3 = new ShellFooterItem(
            ToConsoleDateTime(SiteState.ObservedAtUtc) ?? "Waiting for data");
        _footer4 = new ShellFooterItem($"Outbox: {SiteState.PendingOutboxCount} pending - {SiteState.FailedOutboxCount} failed");
        _footer5 = BuildApiFooterItem();
    }

    private ShellFooterItem BuildLiveFooterItem()
    {
        DateTime? observedAtUtc = SiteState.ObservedAtUtc;

        if (!SiteState.IsInitialized)
            return new ShellFooterItem("Initializing dashboard", ShellFooterIndicator.Warning);

        if (observedAtUtc.HasValue
            && DateTime.UtcNow - observedAtUtc.Value <= LiveLoopFreshnessThreshold
            && SiteState.IsStationConnected)
            return new ShellFooterItem("Live loop active", ShellFooterIndicator.Online);

        if (!SiteState.IsStationConnected)
            return new ShellFooterItem("Station disconnected", ShellFooterIndicator.Offline);

        if (observedAtUtc.HasValue)
            return new ShellFooterItem("Live loop stale", ShellFooterIndicator.Warning);

        return new ShellFooterItem("Waiting for live packets", ShellFooterIndicator.Warning);
    }

    private ShellFooterItem BuildApiFooterItem()
    {
        if (SiteState.PendingOutboxCount > 0 && !string.IsNullOrWhiteSpace(SiteState.LastOutboxError))
            return new ShellFooterItem("API sync failing", ShellFooterIndicator.Offline);

        if (SiteState.PendingOutboxCount > 0)
            return new ShellFooterItem("API sync pending", ShellFooterIndicator.Warning);

        if (SiteState.FailedOutboxCount > 0)
            return new ShellFooterItem("API sync degraded", ShellFooterIndicator.Warning);

        if (SiteState.LastOutboxSentAt.HasValue)
            return new ShellFooterItem("API sync healthy", ShellFooterIndicator.Online);

        return new ShellFooterItem(StationLocationText, ShellFooterIndicator.None);
    }

    private string? ToConsoleDateTime(DateTime? utc) =>
        utc.HasValue
            ? HvoFormat.FooterTimestamp(new DateTimeOffset(utc.Value, TimeSpan.Zero)
                  .ToOffset(SiteState.ConsoleUtcOffset).UtcDateTime)
            : null;
}

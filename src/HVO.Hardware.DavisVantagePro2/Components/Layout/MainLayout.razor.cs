using System.Globalization;
using HVO.Hardware.DavisVantagePro2.Services;
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

    private MudTheme ShellTheme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2d5fb7",
            Secondary = "#2d8b79",
            Background = "#ecf3fb",
            Surface = "#fbfdff",
            AppbarBackground = "rgba(255,255,255,0)",
            AppbarText = "#13263f",
            DrawerBackground = "#f5f9fd",
            DrawerText = "#1c2d43",
            TextPrimary = "#10233f",
            TextSecondary = "#4f6887"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6da5ff",
            Secondary = "#57bca6",
            Background = "#08111f",
            Surface = "#1f2937",
            AppbarBackground = "rgba(0,0,0,0)",
            AppbarText = "#f8fbff",
            DrawerBackground = "#101826",
            DrawerText = "#d9e3f3",
            TextPrimary = "#f8fbff",
            TextSecondary = "#9fb4d5"
        }
    };

    private string LayoutThemeClass => _shellState.IsDarkMode ? "shell-theme-dark" : "shell-theme-light";

    private string ThemeSelectorIcon => _shellState.IsDarkMode
        ? Icons.Material.Outlined.DarkMode
        : Icons.Material.Outlined.LightMode;

    private string ThemeSelectorLabel => _shellState.IsDarkMode ? "Switch to light theme" : "Switch to dark theme";

    private string StationHardwareDescriptionText => SiteState.StationInfo?.HardwareDescription ?? "Loading console identity";

    private string StationHardwareTypeText => SiteState.StationInfo?.HardwareType.ToString(CultureInfo.InvariantCulture) ?? "-";

    private string StationModelTypeText => SiteState.StationInfo?.ModelType.ToString(CultureInfo.InvariantCulture) ?? "-";

    private string StationFirmwareVersionText => SiteState.StationInfo?.FirmwareVersion ?? "-";

    private string StationFirmwareDateText => SiteState.StationInfo?.FirmwareDate ?? "-";

    private string StationConsoleTimeText => SiteState.StationInfo?.ConsoleTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Waiting for console time";

    private string StationSnapshotStatusText => SiteState.StationInfoSavedAtUtc.HasValue
        ? $"Cached {SiteState.StationInfoSavedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
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

    private void OpenStationInfoDialog()
    {
        _showStationInfoDialog = true;
    }

    private void CloseStationInfoDialog()
    {
        _showStationInfoDialog = false;
    }

    private Variant GetNavLinkVariant(string section)
    {
        return IsCurrentSection(section) ? Variant.Filled : Variant.Text;
    }

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
        return string.Equals(_shellState.CurrentSection, section, StringComparison.Ordinal);
    }

    private static string? GetFooterIndicatorClass(ShellFooterIndicator indicator)
    {
        return indicator switch
        {
            ShellFooterIndicator.Online => "shell-status-dot shell-status-dot-online",
            ShellFooterIndicator.Offline => "shell-status-dot shell-status-dot-offline",
            ShellFooterIndicator.Warning => "shell-status-dot shell-status-dot-warning",
            _ => null
        };
    }

    private async Task RetryInitializationAsync()
    {
        await SiteState.RetryInitializationAsync();
    }

    private void HandleShellStateChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

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
        _shellState.SetFooter(
            BuildLiveFooterItem(),
            new ShellFooterItem(SiteState.StationIdentityText),
            new ShellFooterItem(ToConsoleDateTime(SiteState.ObservedAtUtc) ?? "Waiting for data"),
            new ShellFooterItem($"Outbox: {SiteState.PendingOutboxCount} pending - {SiteState.FailedOutboxCount} failed"),
            BuildApiFooterItem());
    }

    private ShellFooterItem BuildLiveFooterItem()
    {
        DateTime? observedAtUtc = SiteState.ObservedAtUtc;

        if (!SiteState.IsInitialized)
        {
            return new ShellFooterItem("Initializing dashboard", ShellFooterIndicator.Warning);
        }

        if (observedAtUtc.HasValue
            && DateTime.UtcNow - observedAtUtc.Value <= LiveLoopFreshnessThreshold
            && SiteState.IsStationConnected)
        {
            return new ShellFooterItem("Live loop active", ShellFooterIndicator.Online);
        }

        if (!SiteState.IsStationConnected)
        {
            return new ShellFooterItem("Station disconnected", ShellFooterIndicator.Offline);
        }

        if (observedAtUtc.HasValue)
        {
            return new ShellFooterItem("Live loop stale", ShellFooterIndicator.Warning);
        }

        return new ShellFooterItem("Waiting for live packets", ShellFooterIndicator.Warning);
    }

    private ShellFooterItem BuildApiFooterItem()
    {
        if (SiteState.PendingOutboxCount > 0 && !string.IsNullOrWhiteSpace(SiteState.LastOutboxError))
        {
            return new ShellFooterItem("API sync failing", ShellFooterIndicator.Offline);
        }

        if (SiteState.PendingOutboxCount > 0)
        {
            return new ShellFooterItem("API sync pending", ShellFooterIndicator.Warning);
        }

        if (SiteState.FailedOutboxCount > 0)
        {
            return new ShellFooterItem("API sync degraded", ShellFooterIndicator.Warning);
        }

        if (SiteState.LastOutboxSentAt.HasValue)
        {
            return new ShellFooterItem("API sync healthy", ShellFooterIndicator.Online);
        }

        return new ShellFooterItem(StationLocationText, ShellFooterIndicator.None);
    }

    private string? ToConsoleDateTime(DateTime? utc) =>
        utc.HasValue
            ? new DateTimeOffset(utc.Value, TimeSpan.Zero)
                  .ToOffset(SiteState.ConsoleUtcOffset)
                  .ToString("dd MMM yyyy - h:mm:ss tt", CultureInfo.InvariantCulture)
            : null;
}
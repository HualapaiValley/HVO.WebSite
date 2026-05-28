using System.Globalization;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.Gateway.SolarAssistant.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private bool _isDarkMode = true;
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    [Inject] private SolarAssistantGatewayHealthService HealthService { get; set; } = default!;

    [Inject] private SolarAssistantSnapshotWorker SnapshotWorker { get; set; } = default!;

    [Inject] private PowerApiForwarder Forwarder { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

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

    private string LayoutThemeClass => _isDarkMode ? "shell-theme-dark" : "shell-theme-light";

    private string ThemeSelectorIcon => _isDarkMode
        ? Icons.Material.Outlined.DarkMode
        : Icons.Material.Outlined.LightMode;

    private string ThemeSelectorLabel => _isDarkMode ? "Switch to light theme" : "Switch to dark theme";

    private SolarAssistantGatewayHealthSnapshot Health => HealthService.GetSnapshot();

    private string FooterSourceText => string.IsNullOrWhiteSpace(SnapshotWorker.LastSnapshot?.SourceId)
        ? "SolarAssistant gateway"
        : SnapshotWorker.LastSnapshot!.SourceId;

    private string FooterSampleTimeText => SnapshotWorker.LastSnapshotAt.HasValue
        ? SnapshotWorker.LastSnapshotAt.Value.ToLocalTime().ToString("dd MMM yyyy - h:mm:ss tt", CultureInfo.InvariantCulture)
        : "Waiting for data";

    private string FooterOutboxText => $"Outbox: {Forwarder.PendingCount} pending - {Forwarder.FailedCount} failed";

    private string FooterApiText => Forwarder.PendingCount > 0 && !string.IsNullOrWhiteSpace(Forwarder.LastError)
        ? "API sync failing"
        : Forwarder.PendingCount > 0
            ? "API sync pending"
            : Forwarder.FailedCount > 0
                ? "API sync degraded"
                : Forwarder.LastSentAt.HasValue
                    ? "API sync healthy"
                    : "API sync idle";

    private string FooterApiDotClass => Forwarder.PendingCount > 0 && !string.IsNullOrWhiteSpace(Forwarder.LastError)
        ? "shell-status-dot shell-status-dot-offline"
        : Forwarder.PendingCount > 0 || Forwarder.FailedCount > 0
            ? "shell-status-dot shell-status-dot-warning"
            : Forwarder.LastSentAt.HasValue
                ? "shell-status-dot shell-status-dot-online"
                : "shell-status-dot shell-status-dot-warning";

    protected override void OnInitialized()
    {
        _refreshCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    public void Dispose()
    {
        _refreshCts?.Cancel();
        _refreshTimer?.Dispose();
        _refreshCts?.Dispose();
    }

    private string FooterHealthText => Health.State switch
    {
        "healthy" => "Gateway healthy",
        "warning" => $"{Health.Alerts.Count} warning(s)",
        "critical" => $"{Health.Alerts.Count} critical alert(s)",
        _ => "Gateway unknown",
    };

    private string FooterHealthDotClass => Health.State switch
    {
        "healthy" => "shell-status-dot shell-status-dot-online",
        "warning" => "shell-status-dot shell-status-dot-warning",
        "critical" => "shell-status-dot shell-status-dot-offline",
        _ => "shell-status-dot shell-status-dot-warning",
    };

    private Variant GetNavLinkVariant(string route) => IsActiveRoute(route) ? Variant.Filled : Variant.Text;

    private string GetNavLinkClass(string route) => IsActiveRoute(route)
        ? "shell-nav-link shell-nav-link-current"
        : "shell-nav-link";

    private bool IsActiveRoute(string route)
    {
        var relativePath = Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
        return route == "monitor"
            ? relativePath is "" or "monitor"
            : string.Equals(relativePath, route, StringComparison.OrdinalIgnoreCase);
    }

    private void OnThemeModeChanged(bool useDarkMode)
    {
        _isDarkMode = useDarkMode;
    }

    private void ToggleTheme()
    {
        _isDarkMode = !_isDarkMode;
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        if (_refreshTimer is null)
            return;

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(ct))
                await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

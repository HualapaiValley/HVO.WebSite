using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using HVO.Gateway.SolarAssistant.Workers;
using HVO.WebSite.Themes.Components.Format;
using HVO.WebSite.Themes.Components.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.Gateway.SolarAssistant.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private readonly ShellLayoutState _shellState = new();
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    [Inject] private SolarAssistantGatewayHealthService HealthService { get; set; } = default!;
    [Inject] private SolarAssistantSnapshotWorker SnapshotWorker { get; set; } = default!;
    [Inject] private PowerApiForwarder Forwarder { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private ShellFooterItem _footer1 = new("SolarAssistant gateway");
    private ShellFooterItem _footer2 = new("SolarAssistant gateway");
    private ShellFooterItem _footer3 = new("Waiting for data", ShellFooterIndicator.Warning);
    private ShellFooterItem _footer4 = new("Outbox");
    private ShellFooterItem _footer5 = new("API sync");

    private string ThemeSelectorIcon => _shellState.IsDarkMode
        ? Icons.Material.Outlined.DarkMode
        : Icons.Material.Outlined.LightMode;

    private string ThemeSelectorLabel => _shellState.IsDarkMode ? "Switch to light theme" : "Switch to dark theme";

    private SolarAssistantGatewayHealthSnapshot Health => HealthService.GetSnapshot();

    protected override void OnInitialized()
    {
        _shellState.Changed += HandleShellStateChanged;
        _refreshCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        UpdateFooter();
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    public void Dispose()
    {
        _shellState.Changed -= HandleShellStateChanged;
        _refreshCts?.Cancel();
        _refreshTimer?.Dispose();
        _refreshCts?.Dispose();
    }

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
        _shellState.SetTheme(useDarkMode);
    }

    private void ToggleTheme()
    {
        _shellState.ToggleTheme();
    }

    private void HandleShellStateChanged()
    {
        UpdateFooter();
        _ = InvokeAsync(StateHasChanged);
    }

    private void UpdateFooter()
    {
        var health = Health;
        var source = string.IsNullOrWhiteSpace(SnapshotWorker.LastSnapshot?.SourceId)
            ? "SolarAssistant gateway"
            : SnapshotWorker.LastSnapshot!.SourceId;

        _footer1 = new ShellFooterItem(
            health.State switch
            {
                "healthy" => "Gateway healthy",
                "warning" => $"{health.Alerts.Count} warning(s)",
                "critical" => $"{health.Alerts.Count} critical alert(s)",
                _ => "Gateway unknown",
            },
            health.State switch
            {
                "healthy" => ShellFooterIndicator.Online,
                "warning" => ShellFooterIndicator.Warning,
                "critical" => ShellFooterIndicator.Offline,
                _ => ShellFooterIndicator.Warning,
            });
        _footer2 = new ShellFooterItem(source);
        _footer3 = new ShellFooterItem(
            SnapshotWorker.LastSnapshotAt.HasValue
                ? HvoFormat.FooterTimestamp(SnapshotWorker.LastSnapshotAt)
                : "Waiting for data",
            SnapshotWorker.LastSnapshotAt.HasValue ? ShellFooterIndicator.Online : ShellFooterIndicator.Warning);
        _footer4 = new ShellFooterItem($"Outbox: {Forwarder.PendingCount} pending - {Forwarder.FailedCount} failed");
        _footer5 = new ShellFooterItem(
            Forwarder.PendingCount > 0 && !string.IsNullOrWhiteSpace(Forwarder.LastError)
                ? "API sync failing"
                : Forwarder.PendingCount > 0
                    ? "API sync pending"
                    : Forwarder.FailedCount > 0
                        ? "API sync degraded"
                        : Forwarder.LastSentAt.HasValue
                            ? "API sync healthy"
                            : "API sync idle",
            Forwarder.PendingCount > 0 && !string.IsNullOrWhiteSpace(Forwarder.LastError)
                ? ShellFooterIndicator.Offline
                : Forwarder.PendingCount > 0 || Forwarder.FailedCount > 0
                    ? ShellFooterIndicator.Warning
                    : Forwarder.LastSentAt.HasValue
                        ? ShellFooterIndicator.Online
                        : ShellFooterIndicator.Warning);
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        if (_refreshTimer is null)
            return;

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(ct))
            {
                UpdateFooter();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

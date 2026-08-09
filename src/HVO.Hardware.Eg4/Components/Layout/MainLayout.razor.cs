using HVO.Hardware.Eg4.Dashboard;
using HVO.WebSite.Themes.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace HVO.Hardware.Eg4.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private readonly ShellLayoutState _shellState = new();
    [Inject] private IEg4GatewayDashboardState DashboardState { get; set; } = default!;
    [Inject] private ILogger<MainLayout> Logger { get; set; } = default!;
    private ShellFooterItem _footer1 = new("EG4 gateway");
    private ShellFooterItem _footer2 = new("No devices configured");
    private ShellFooterItem _footer3 = new("Waiting for data", ShellFooterIndicator.Warning);
    private ShellFooterItem _footer4 = new("Outbox idle");
    private ShellFooterItem _footer5 = new("Collector pending", ShellFooterIndicator.Warning);
    private string ThemeSelectorIcon => _shellState.IsDarkMode ? Icons.Material.Outlined.DarkMode : Icons.Material.Outlined.LightMode;
    private string ThemeSelectorLabel => _shellState.IsDarkMode ? "Switch to light theme" : "Switch to dark theme";

    protected override void OnInitialized()
    {
        _shellState.Changed += HandleChanged;
        DashboardState.Changed += HandleChanged;
        UpdateFooter();
    }

    public void Dispose()
    {
        _shellState.Changed -= HandleChanged;
        DashboardState.Changed -= HandleChanged;
    }

    private void OnThemeModeChanged(bool value) => _shellState.SetTheme(value);
    private void ToggleTheme() => _shellState.ToggleTheme();

    private void HandleChanged() => _ = DispatchChangedAsync();

    private async Task DispatchChangedAsync()
    {
        try
        {
            await InvokeAsync(() =>
            {
                UpdateFooter();
                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            Logger.LogDebug(exception, "EG4 layout update could not be dispatched to the circuit");
        }
    }

    private void UpdateFooter()
    {
        var snapshot = DashboardState.GetSnapshot();
        _footer1 = snapshot.HealthState switch
        {
            Eg4DashboardHealthState.Healthy => new ShellFooterItem("Fleet online", ShellFooterIndicator.Online),
            Eg4DashboardHealthState.Degraded => new ShellFooterItem("Fleet degraded", ShellFooterIndicator.Warning),
            Eg4DashboardHealthState.Offline => new ShellFooterItem("Fleet offline", ShellFooterIndicator.Offline),
            _ => new ShellFooterItem("No enabled devices", ShellFooterIndicator.Warning),
        };
        _footer2 = new ShellFooterItem($"{snapshot.ConfiguredCount} configured - {snapshot.OnlineCount} online");
        _footer3 = new ShellFooterItem(snapshot.RefreshedAtUtc.HasValue ? "Telemetry refreshed" : "Waiting for data",
            snapshot.RefreshedAtUtc.HasValue ? ShellFooterIndicator.Online : ShellFooterIndicator.Warning);
        _footer4 = new ShellFooterItem($"Outbox: {snapshot.Outbox.PendingCount} pending - {snapshot.Outbox.FailedCount} failed");
        _footer5 = new ShellFooterItem(snapshot.Outbox.ForwardingStatus,
            snapshot.Outbox.FailedCount > 0 ? ShellFooterIndicator.Offline : ShellFooterIndicator.Warning);
    }
}

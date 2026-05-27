using System.Globalization;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Workers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace HVO.Hardware.JkBms.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private static readonly TimeSpan FreshPollThreshold = TimeSpan.FromSeconds(90);
    private readonly ShellLayoutState _shellState = new();

    [Inject] private BmsPollerWorker Poller { get; set; } = default!;
    [Inject] private ForwarderCoordinator Forwarder { get; set; } = default!;
    [Inject] private IOptions<OutboxOptions> OutboxOptionsAccessor { get; set; } = default!;

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
            TextPrimary = "#f8fbff",
            TextSecondary = "#9fb4d5"
        }
    };

    private string LayoutThemeClass => _shellState.IsDarkMode ? "shell-theme-dark" : "shell-theme-light";
    private string ThemeSelectorIcon => _shellState.IsDarkMode ? Icons.Material.Outlined.DarkMode : Icons.Material.Outlined.LightMode;
    private string ThemeSelectorLabel => _shellState.IsDarkMode ? "Switch to light theme" : "Switch to dark theme";
    private OutboxOptions OutboxOptions => OutboxOptionsAccessor.Value;

    protected override void OnInitialized()
    {
        _shellState.Changed += HandleShellStateChanged;
        Poller.DeviceStateChanged += HandlePollerStateChanged;
        Forwarder.SweepCompleted += HandlePollerStateChanged;
        UpdateFooter();
    }

    public void Dispose()
    {
        _shellState.Changed -= HandleShellStateChanged;
        Poller.DeviceStateChanged -= HandlePollerStateChanged;
        Forwarder.SweepCompleted -= HandlePollerStateChanged;
    }

    private void OnThemeModeChanged(bool useDarkMode)
    {
        _shellState.SetTheme(useDarkMode);
    }

    private void ToggleTheme()
    {
        _shellState.ToggleTheme();
    }

    private Variant GetNavLinkVariant(string section)
        => IsCurrentSection(section) ? Variant.Filled : Variant.Text;

    private string GetNavLinkClass(string section)
        => IsCurrentSection(section) ? "shell-nav-link shell-nav-link-current" : "shell-nav-link";

    private bool IsCurrentSection(string section)
        => string.Equals(_shellState.CurrentSection, section, StringComparison.Ordinal);

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

    private void HandleShellStateChanged()
    {
        UpdateFooter();
        _ = InvokeAsync(StateHasChanged);
    }

    private void HandlePollerStateChanged()
    {
        UpdateFooter();
        _ = InvokeAsync(StateHasChanged);
    }

    private void UpdateFooter()
    {
        var activeDevices = Poller.DeviceStates.Where(state => state.LatestReading is not null).ToList();
        var connectedDevices = Poller.DeviceStates.Count(state => state.IsSessionConnected);
        var latestPoll = Poller.DeviceStates
            .Where(state => state.LastPollAt.HasValue)
            .OrderByDescending(state => state.LastPollAt)
            .FirstOrDefault()
            ?.LastPollAt;

        _shellState.SetFooter(
            BuildConnectivityFooterItem(connectedDevices, Poller.DeviceStates.Count),
            new ShellFooterItem($"JK fleet: {Poller.DeviceStates.Count} bank(s)"),
            BuildTimestampFooterItem(latestPoll),
            new ShellFooterItem($"Outbox: {Forwarder.PendingCount} pending - {Forwarder.FailedCount} failed"),
            BuildApiFooterItem());
    }

    private static ShellFooterItem BuildConnectivityFooterItem(int connectedDevices, int totalDevices)
    {
        if (totalDevices == 0)
            return new ShellFooterItem("No banks configured", ShellFooterIndicator.Warning);

        if (connectedDevices == totalDevices)
            return new ShellFooterItem($"{connectedDevices}/{totalDevices} banks connected", ShellFooterIndicator.Online);

        if (connectedDevices == 0)
            return new ShellFooterItem("All banks disconnected", ShellFooterIndicator.Offline);

        return new ShellFooterItem($"{connectedDevices}/{totalDevices} banks connected", ShellFooterIndicator.Warning);
    }

    private static ShellFooterItem BuildTimestampFooterItem(DateTime? latestPoll)
    {
        if (!latestPoll.HasValue)
            return new ShellFooterItem("Waiting for data", ShellFooterIndicator.Warning);

        var indicator = DateTime.UtcNow - latestPoll.Value <= FreshPollThreshold
            ? ShellFooterIndicator.Online
            : ShellFooterIndicator.Warning;

        return new ShellFooterItem(
            latestPoll.Value
                .ToLocalTime()
                .ToString("dd MMM yyyy - h:mm:ss tt", CultureInfo.InvariantCulture),
            indicator);
    }

    private ShellFooterItem BuildApiFooterItem()
    {
        var evaluation = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(
                PendingCount: Forwarder.PendingCount,
                FailedCount: Forwarder.FailedCount,
                LastSentAtUtc: Forwarder.LastSentAt,
                LastBatchCount: Forwarder.LastBatchCount,
                LastError: Forwarder.LastError),
            new EdgeOutboxHealthOptions(
                PendingWarningCount: OutboxOptions.PendingWarningCount,
                FailedCriticalCount: OutboxOptions.FailedCriticalCount));

        return evaluation.CurrentSyncState switch
        {
            EdgeOutboxSyncState.Failing => new ShellFooterItem("API sync failing", ShellFooterIndicator.Offline),
            EdgeOutboxSyncState.Pending => new ShellFooterItem("API sync pending", ShellFooterIndicator.Warning),
            EdgeOutboxSyncState.Degraded => new ShellFooterItem("API sync degraded", ShellFooterIndicator.Warning),
            EdgeOutboxSyncState.Healthy => new ShellFooterItem("API sync healthy", ShellFooterIndicator.Online),
            _ => new ShellFooterItem("API sync idle"),
        };
    }
}

using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Workers;
using HVO.WebSite.Themes.Components.Format;
using HVO.WebSite.Themes.Components.Layout;
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
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private ShellFooterItem _footer1 = new("JK BMS");
    private ShellFooterItem _footer2 = new("Fleet summary");
    private ShellFooterItem _footer3 = new("Waiting for data", ShellFooterIndicator.Warning);
    private ShellFooterItem _footer4 = new("Bank telemetry");
    private ShellFooterItem _footer5 = new("API sync");

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
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
        return section switch
        {
            "Overview" => path is "" or "monitor",
            "Banks" => path.StartsWith("device", StringComparison.OrdinalIgnoreCase),
            _ => false
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

        _footer1 = BuildConnectivityFooterItem(connectedDevices, Poller.DeviceStates.Count);
        _footer2 = new ShellFooterItem($"JK fleet: {Poller.DeviceStates.Count} bank(s)");
        _footer3 = BuildTimestampFooterItem(latestPoll);
        _footer4 = new ShellFooterItem($"Outbox: {Forwarder.PendingCount} pending - {Forwarder.FailedCount} failed");
        _footer5 = BuildApiFooterItem();
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

        return new ShellFooterItem(HvoFormat.FooterTimestamp(latestPoll), indicator);
    }

    private ShellFooterItem BuildApiFooterItem()
    {
        var pendingCount = Forwarder.PendingCount;
        var evaluation = EdgeOutboxHealthEvaluator.Evaluate(
            new EdgeOutboxObservation(
                PendingCount: pendingCount,
                FailedCount: Forwarder.FailedCount,
                LastSentAtUtc: Forwarder.LastSentAt,
                LastBatchCount: Forwarder.LastBatchCount,
                LastError: pendingCount > 0 ? Forwarder.LastError : null,
                PermanentFailedCount: Forwarder.FailedCount),
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

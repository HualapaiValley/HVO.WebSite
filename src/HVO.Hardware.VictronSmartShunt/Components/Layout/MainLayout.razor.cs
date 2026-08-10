using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.Workers;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using HVO.WebSite.Themes.Components.Format;
using HVO.WebSite.Themes.Components.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.Hardware.VictronSmartShunt.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private static readonly TimeSpan SampleFreshnessThreshold = TimeSpan.FromSeconds(20);
    private readonly ShellLayoutState _shellState = new();
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    [Inject] private SmartShuntGatewayHealthService HealthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private SmartShuntWorker Worker { get; set; } = default!;
    [Inject] private PowerApiForwarder Forwarder { get; set; } = default!;
    [Inject] private Microsoft.Extensions.Options.IOptions<SmartShuntOptions> OptionsAccessor { get; set; } = default!;
    [Inject] private HvoDisplayTimeZone DisplayTimeZone { get; set; } = default!;

    private ShellFooterItem _footer1 = new("SmartShunt");
    private ShellFooterItem _footer2 = new("Victron battery monitor");
    private ShellFooterItem _footer3 = new("Waiting for sample", ShellFooterIndicator.Warning);
    private ShellFooterItem _footer4 = new("Public telemetry");
    private ShellFooterItem _footer5 = new("API sync");

    private SmartShuntOptions Options => OptionsAccessor.Value;

    private string ThemeSelectorIcon => _shellState.IsDarkMode ? Icons.Material.Outlined.DarkMode : Icons.Material.Outlined.LightMode;

    private string ThemeSelectorLabel => _shellState.IsDarkMode ? "Switch to light theme" : "Switch to dark theme";

    protected override void OnInitialized()
    {
        _shellState.Changed += HandleShellStateChanged;
        Navigation.LocationChanged += HandleLocationChanged;
        _refreshCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        UpdateFooter();
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    public void Dispose()
    {
        _shellState.Changed -= HandleShellStateChanged;
        Navigation.LocationChanged -= HandleLocationChanged;
        _refreshCts?.Cancel();
        _refreshTimer?.Dispose();
        _refreshCts?.Dispose();
    }

    private Variant GetNavLinkVariant(string section)
    {
        return IsCurrentSection(section) ? Variant.Filled : Variant.Text;
    }

    private string GetNavLinkClass(string section)
    {
        return IsCurrentSection(section)
            ? "shell-nav-link shell-nav-link-current"
            : "shell-nav-link";
    }

    private bool IsCurrentSection(string section)
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
        return section switch
        {
            "Overview" => path is "" or "monitor",
            "Telemetry" => path.StartsWith("telemetry", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private void OnThemeModeChanged(bool useDarkMode)
    {
        _shellState.SetTheme(useDarkMode);
    }

    private void ToggleTheme()
    {
        _shellState.ToggleTheme();
    }

    private void HandleLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        UpdateFooter();
        _ = InvokeAsync(StateHasChanged);
    }

    private void HandleShellStateChanged()
    {
        UpdateFooter();
        _ = InvokeAsync(StateHasChanged);
    }

    private void UpdateFooter()
    {
        var sample = Worker.LastSnapshot;
        var health = HealthService.GetSnapshot();

        _footer1 = BuildHealthFooterItem(health);
        _footer2 = new ShellFooterItem(string.IsNullOrWhiteSpace(Options.DeviceId) ? "SmartShunt device" : Options.DeviceId);
        _footer3 = BuildSampleTimestampFooterItem(sample);
        _footer4 = new ShellFooterItem($"Outbox: {Forwarder.PendingCount} pending - {Forwarder.FailedCount} failed");
        _footer5 = BuildApiFooterItem();
    }

    private static ShellFooterItem BuildHealthFooterItem(SmartShuntGatewayHealthSnapshot health)
    {
        return health.State switch
        {
            "healthy" => new ShellFooterItem("Gateway healthy", ShellFooterIndicator.Online),
            "warning" => new ShellFooterItem("Gateway warning", ShellFooterIndicator.Warning),
            "critical" => new ShellFooterItem("Gateway critical", ShellFooterIndicator.Offline),
            _ => new ShellFooterItem("Gateway unknown", ShellFooterIndicator.Warning)
        };
    }

    private ShellFooterItem BuildSampleTimestampFooterItem(SmartShuntDeviceSnapshot? sample)
    {
        if (sample is null)
            return new ShellFooterItem("Waiting for sample", ShellFooterIndicator.Warning);

        var indicator = DateTime.UtcNow - sample.RecordedAtUtc <= SampleFreshnessThreshold
            ? ShellFooterIndicator.Online
            : ShellFooterIndicator.Warning;

        return new ShellFooterItem(
            $"{HvoFormat.FooterTimestamp(sample.RecordedAtUtc, DisplayTimeZone.TimeZone)} {DisplayTimeZone.Label}",
            indicator);
    }

    private ShellFooterItem BuildApiFooterItem()
    {
        if (Forwarder.PendingCount > 0 && !string.IsNullOrWhiteSpace(Forwarder.LastError))
            return new ShellFooterItem("API sync failing", ShellFooterIndicator.Offline);

        if (Forwarder.PendingCount > 0)
            return new ShellFooterItem("API sync pending", ShellFooterIndicator.Warning);

        if (Forwarder.FailedCount > 0)
            return new ShellFooterItem("API sync degraded", ShellFooterIndicator.Warning);

        if (Forwarder.LastSentAt.HasValue)
            return new ShellFooterItem("API sync healthy", ShellFooterIndicator.Online);

        return new ShellFooterItem("API sync idle");
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

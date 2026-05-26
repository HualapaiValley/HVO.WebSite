using Microsoft.AspNetCore.Components;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.Workers;
using MudBlazor;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;

namespace HVO.Hardware.VictronSmartShunt.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private static readonly TimeSpan SampleFreshnessThreshold = TimeSpan.FromSeconds(20);
    private readonly ShellLayoutState _shellState = new();

    [Inject] private SmartShuntGatewayHealthService HealthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private SmartShuntWorker Worker { get; set; } = default!;
    [Inject] private PowerApiForwarder Forwarder { get; set; } = default!;
    [Inject] private Microsoft.Extensions.Options.IOptions<SmartShuntOptions> OptionsAccessor { get; set; } = default!;

    private MudTheme ShellTheme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2d5fb7",
            Secondary = "#f28c28",
            Background = "#ecf3fb",
            Surface = "#fbfdff"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6da5ff",
            Secondary = "#f9a94b",
            Background = "#08111f",
            Surface = "#1f2937"
        }
    };

    private SmartShuntOptions Options => OptionsAccessor.Value;

    private string LayoutThemeClass => _shellState.IsDarkMode ? "shell-theme-dark" : "shell-theme-light";

    private string ThemeSelectorIcon => _shellState.IsDarkMode ? Icons.Material.Outlined.DarkMode : Icons.Material.Outlined.LightMode;

    private string ThemeSelectorLabel => _shellState.IsDarkMode ? "Switch to light theme" : "Switch to dark theme";

    protected override void OnInitialized()
    {
        _shellState.Changed += HandleShellStateChanged;
        Navigation.LocationChanged += HandleLocationChanged;
        UpdateFooter();
    }

    public void Dispose()
    {
        _shellState.Changed -= HandleShellStateChanged;
        Navigation.LocationChanged -= HandleLocationChanged;
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
        var sampleState = BuildSampleFooterItem(sample);
        var modeText = Options.PublicOnly
            ? "Public-only mode"
            : Options.EnablePrivateEnrichment
                ? "Public + private mode"
                : "Custom mode";

        _shellState.SetFooter(
            BuildHealthFooterItem(health),
            new ShellFooterItem(Options.DeviceId),
            sampleState,
            new ShellFooterItem(modeText),
            BuildApiFooterItem());
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

    private static ShellFooterItem BuildSampleFooterItem(SmartShuntDeviceSnapshot? sample)
    {
        if (sample is null)
            return new ShellFooterItem("Waiting for live sample", ShellFooterIndicator.Warning);

        return DateTime.UtcNow - sample.RecordedAtUtc <= SampleFreshnessThreshold
            ? new ShellFooterItem($"Live {sample.RecordedAtUtc.ToLocalTime():HH:mm:ss}", ShellFooterIndicator.Online)
            : new ShellFooterItem($"Sample stale {sample.RecordedAtUtc.ToLocalTime():HH:mm:ss}", ShellFooterIndicator.Warning);
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
}

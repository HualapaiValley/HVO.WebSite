using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.Hardware.DavisVantagePro2.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private readonly ShellLayoutState _shellState = new();

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

    protected override void OnInitialized()
    {
        _shellState.Changed += HandleShellStateChanged;
    }

    public void Dispose()
    {
        _shellState.Changed -= HandleShellStateChanged;
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

    private void HandleShellStateChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }
}
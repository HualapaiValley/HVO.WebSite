using Microsoft.AspNetCore.Components;
using MudBlazor;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;

namespace HVO.Hardware.VictronSmartShunt.Components.Layout;

public partial class MainLayout : LayoutComponentBase
{
    private bool _isDarkMode = true;

    [Inject] private SmartShuntGatewayHealthService HealthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

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

    private string ThemeSelectorIcon => _isDarkMode ? Icons.Material.Outlined.DarkMode : Icons.Material.Outlined.LightMode;

    private string FooterHealthText => HealthService.GetSnapshot().State switch
    {
        "healthy" => "Gateway healthy",
        "warning" => "Gateway warning",
        "critical" => "Gateway critical",
        _ => "Gateway unknown"
    };

    private Variant GetNavVariant(string route)
    {
        var relativePath = Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
        return string.Equals(relativePath, route, StringComparison.OrdinalIgnoreCase) || (route.Length == 0 && relativePath.Length == 0)
            ? Variant.Filled
            : Variant.Text;
    }

    private void OnThemeModeChanged(bool useDarkMode) => _isDarkMode = useDarkMode;
    private void ToggleTheme() => _isDarkMode = !_isDarkMode;
}

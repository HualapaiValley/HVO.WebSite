using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.WebSite.Themes.Components.Layout;

public partial class HvoAdminLayout : LayoutComponentBase
{
    private bool _drawerOpen = true;

    private DrawerVariant _drawerVariant = DrawerVariant.Persistent;

    [Parameter] public RenderFragment? SidebarItems { get; set; }

    [Parameter] public RenderFragment? AppBarActions { get; set; }

    [Parameter] public bool IsDarkMode { get; set; } = true;

    [Parameter] public EventCallback<bool> IsDarkModeChanged { get; set; }

    [Parameter] public MudTheme? Theme { get; set; }

    private MudTheme EffectiveTheme => Theme ?? HvoTheme.Create();

    private string LayoutThemeClass => IsDarkMode ? "shell-theme-dark" : "shell-theme-light";

    private void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }
}

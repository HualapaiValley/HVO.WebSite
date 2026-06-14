using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.WebSite.Themes.Components.Layout;

public partial class HvoPublicLayout : LayoutComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public RenderFragment? NavItems { get; set; }

    [Parameter] public RenderFragment? AuthSection { get; set; }

    [Parameter] public string? BrandTitle { get; set; }

    [Parameter] public string? BrandSubtitle { get; set; }

    [Parameter] public MudTheme? Theme { get; set; }

    [Parameter] public bool IsDarkMode { get; set; } = true;

    [Parameter] public EventCallback<bool> IsDarkModeChanged { get; set; }

    public string LayoutThemeClass => IsDarkMode ? "shell-theme-dark" : "shell-theme-light";

    private MudTheme EffectiveTheme => Theme ?? HvoTheme.Create();
}

using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.WebSite.Themes.Components.Layout;

public partial class HvoPublicLayout : LayoutComponentBase
{
    [Parameter] public RenderFragment? NavItems { get; set; }

    [Parameter] public RenderFragment? AuthSection { get; set; }

    [Parameter] public string? BrandTitle { get; set; }

    [Parameter] public string? BrandSubtitle { get; set; }

    [Parameter] public MudTheme? Theme { get; set; }

    private MudTheme EffectiveTheme => Theme ?? HvoTheme.Create();
}

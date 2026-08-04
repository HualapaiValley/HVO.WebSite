using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HVO.WebSite.Themes.Components.Layout;

public partial class HvoGatewayLayout : LayoutComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string Subtitle { get; set; } = "GATEWAY DASHBOARD";

    [Parameter] public RenderFragment? NavItems { get; set; }

    [Parameter] public RenderFragment? AppBarActions { get; set; }

    [Parameter] public ShellFooterItem? FooterSlot1 { get; set; }

    [Parameter] public ShellFooterItem? FooterSlot2 { get; set; }

    [Parameter] public ShellFooterItem? FooterSlot3 { get; set; }

    [Parameter] public ShellFooterItem? FooterSlot4 { get; set; }

    [Parameter] public ShellFooterItem? FooterSlot5 { get; set; }

    [Parameter] public bool IsDarkMode { get; set; } = true;

    [Parameter] public EventCallback<bool> IsDarkModeChanged { get; set; }

    [Parameter] public MudTheme? Theme { get; set; }

    [Parameter] public string? FrameModifierClass { get; set; }

    private MudTheme EffectiveTheme => Theme ?? HvoTheme.Create();

    private string LayoutThemeClass => IsDarkMode ? "shell-theme-dark" : "shell-theme-light";

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
}

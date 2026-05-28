using Microsoft.AspNetCore.Components;

namespace HVO.Gateway.SolarAssistant.Components.Pages;

public partial class CardHead
{
    [Parameter] public string Eyebrow { get; set; } = string.Empty;

    [Parameter] public string Title { get; set; } = string.Empty;

    [Parameter] public string? LinkHref { get; set; }

    [Parameter] public string? LinkText { get; set; }
}

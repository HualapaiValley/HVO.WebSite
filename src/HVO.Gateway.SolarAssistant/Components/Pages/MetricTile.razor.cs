using Microsoft.AspNetCore.Components;

namespace HVO.Gateway.SolarAssistant.Components.Pages;

public partial class MetricTile
{
    [Parameter] public string Label { get; set; } = string.Empty;

    [Parameter] public string Value { get; set; } = "--";
}

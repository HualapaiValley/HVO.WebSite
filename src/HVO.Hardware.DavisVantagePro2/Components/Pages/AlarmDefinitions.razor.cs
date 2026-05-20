using HVO.Hardware.DavisVantagePro2.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class AlarmDefinitions
{
    private const string PageHeadingText = "Alarm definitions";
    private const string PageSummaryText = "Prototype alarm rules and console threshold editors presented inside the shared Davis shell frame.";

    [CascadingParameter] private ShellLayoutState? ShellLayoutState { get; set; }

    protected override void OnInitialized()
    {
        UpdateShell();
    }

    protected override void OnParametersSet()
    {
        UpdateShell();
    }

    private void UpdateShell()
    {
        ShellLayoutState?.SetPage("Alerts", PageHeadingText, PageSummaryText);
    }
}
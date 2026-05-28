namespace HVO.Hardware.VictronSmartShunt.Components.Pages;

public partial class Telemetry
{
    private const string PageHeadingText = "SmartShunt telemetry inventory";
    private const string PageSummaryText = "Expanded field inventory showing which values are live, private-only, disabled by configuration, or simply not reported by the current device session.";

    protected override void OnParametersSet()
    {
        ShellLayoutState?.SetPage("Telemetry", PageHeadingText, PageSummaryText);
    }
}

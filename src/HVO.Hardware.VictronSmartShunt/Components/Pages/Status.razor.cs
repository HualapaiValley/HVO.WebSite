namespace HVO.Hardware.VictronSmartShunt.Components.Pages;

public partial class Status
{
    private const string PageHeadingText = "Victron SmartShunt overview";
    private const string PageSummaryText = "Live battery telemetry and gateway health framed in the same shell, theme, and navigation pattern as the Davis dashboard.";
    private string SocGaugeStyle => GaugeStyle(ClampPercent(LatestSnapshot?.StateOfChargePercent, 0, 100), "var(--hvo-accent-success)");
    private string VoltageGaugeStyle => GaugeStyle(ClampPercent(LatestSnapshot?.VoltageV, 48, 58), "var(--hvo-accent-blue)");
    private string PowerGaugeStyle => GaugeStyle(ClampPercent(LatestSnapshot?.PowerW is double powerW ? Math.Abs(powerW) : null, 0, 3000), "var(--hvo-accent-amber)");
}

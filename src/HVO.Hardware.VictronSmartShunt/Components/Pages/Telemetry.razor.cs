using HVO.WebSite.Themes.Components.Charts;

namespace HVO.Hardware.VictronSmartShunt.Components.Pages;

public partial class Telemetry
{
    private const string PageHeadingText = "SmartShunt telemetry inventory";
    private const string PageSummaryText = "Expanded field inventory showing which values are live, private-only, disabled by configuration, or simply not reported by the current device session.";
    private static readonly string[] _telemetryLabels = { "Voltage", "Current", "Power" };

    private List<HvoChartDataset> _telemetryDatasets
    {
        get
        {
            var snap = LatestSnapshot;
            if (snap is null) return new();

            return new()
            {
                new HvoChartDataset("Value",
                    new double[] { snap.VoltageV ?? 0, snap.CurrentA ?? 0, snap.PowerW ?? 0 },
                    BorderColor: "#6da5ff", BackgroundColor: "rgba(109,165,255,0.25)", BorderWidth: 1)
            };
        }
    }
}

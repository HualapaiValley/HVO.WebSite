using System.Globalization;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using Microsoft.AspNetCore.Components;

namespace HVO.Gateway.SolarAssistant.Components.Pages;

public partial class PowerHistoryChart
{
    [Parameter] public string Eyebrow { get; set; } = "History";

    [Parameter] public string Title { get; set; } = string.Empty;

    [Parameter] public string LineClass { get; set; } = "solar-history-line-cyan";

    [Parameter] public IReadOnlyList<PowerSnapshotHistoryPoint> Points { get; set; } = [];

    [Parameter] public Func<PowerSnapshotHistoryPoint, double?> ValueSelector { get; set; } = _ => null;

    private IReadOnlyList<ChartPoint> ChartPoints => BuildChartPoints();

    private bool HasSeries => ChartPoints.Count >= 2;

    private string PathData => string.Join(" ", ChartPoints.Select((p, i) => $"{(i == 0 ? "M" : "L")} {p.X.ToString("0.###", CultureInfo.InvariantCulture)} {p.Y.ToString("0.###", CultureInfo.InvariantCulture)}"));

    private ChartPoint LatestPoint => ChartPoints.Count == 0 ? new ChartPoint(28, 116, null) : ChartPoints[^1];

    private string LatestPointX => LatestPoint.X.ToString("0.###", CultureInfo.InvariantCulture);

    private string LatestPointY => LatestPoint.Y.ToString("0.###", CultureInfo.InvariantCulture);

    private double? LatestValue => Points.Select(ValueSelector).LastOrDefault(v => v.HasValue);

    private string HistorySummary => Points.Count == 0
        ? "No samples"
        : $"{Points.Count} samples";

    private string RangeSummary
    {
        get
        {
            var values = Points.Select(ValueSelector).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return values.Length == 0 ? "--" : $"{FormatWatts(values.Min())} to {FormatWatts(values.Max())}";
        }
    }

    private IReadOnlyList<ChartPoint> BuildChartPoints()
    {
        var values = Points
            .Select(p => new { p.RecordedAtUtc, Value = ValueSelector(p) })
            .Where(p => p.Value.HasValue)
            .ToArray();

        if (values.Length < 2)
            return [];

        var minTime = values[0].RecordedAtUtc;
        var maxTime = values[^1].RecordedAtUtc;
        var totalSeconds = Math.Max(1, (maxTime - minTime).TotalSeconds);
        var minValue = values.Min(p => p.Value!.Value);
        var maxValue = values.Max(p => p.Value!.Value);
        var valueRange = Math.Max(1, maxValue - minValue);

        return values.Select(p =>
        {
            var x = 28 + ((p.RecordedAtUtc - minTime).TotalSeconds / totalSeconds * 318);
            var y = 132 - ((p.Value!.Value - minValue) / valueRange * 112);
            return new ChartPoint(x, y, p.Value.Value);
        }).ToArray();
    }

    private static string FormatWatts(double? value) => value.HasValue ? $"{value.Value:0} W" : "--";

    private readonly record struct ChartPoint(double X, double Y, double? Value);
}

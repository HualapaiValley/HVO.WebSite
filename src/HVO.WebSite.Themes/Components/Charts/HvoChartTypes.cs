namespace HVO.WebSite.Themes.Components.Charts;

public enum HvoChartType
{
    Line,
    Bar,
    Doughnut,
    PolarArea,
    Bubble
}

/// <summary>
/// Dataset for <see cref="HvoChart"/>.
/// Use <c>null</c> entries in <see cref="Data"/> to represent missing / gap values.
/// Chart.js renders them as breaks in the line when <c>SpanGaps = false</c>.
/// </summary>
public sealed record HvoChartDataset(
    string Label,
    IReadOnlyList<double?> Data,
    string? BorderColor = null,
    string? BackgroundColor = null,
    double BorderWidth = 3,
    double PointRadius = 3,
    bool Fill = false,
    double? Tension = null
);

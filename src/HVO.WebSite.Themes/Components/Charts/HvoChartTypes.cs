namespace HVO.WebSite.Themes.Components.Charts;

public enum HvoChartType
{
    Line,
    Bar,
    Doughnut,
    PolarArea,
    Bubble
}

public sealed record HvoChartDataset(
    string Label,
    IReadOnlyList<double> Data,
    string? BorderColor = null,
    string? BackgroundColor = null,
    double BorderWidth = 3,
    double PointRadius = 3,
    bool Fill = false
);

using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Guide steps history containing RMS statistics and guide step data
/// </summary>
public record GuideStepsHistory
{
    [JsonPropertyName("RMS")]
    public GuideRMS RMS { get; init; } = new();

    [JsonPropertyName("Interval")]
    public int Interval { get; init; }

    [JsonPropertyName("MaxY")]
    public int MaxY { get; init; }

    [JsonPropertyName("MinY")]
    public int MinY { get; init; }

    [JsonPropertyName("MaxDurationY")]
    public int MaxDurationY { get; init; }

    [JsonPropertyName("MinDurationY")]
    public int MinDurationY { get; init; }

    [JsonPropertyName("GuideSteps")]
    public List<GuideStepHistory> GuideSteps { get; init; } = new();

    [JsonPropertyName("HistorySize")]
    public int HistorySize { get; init; }

    [JsonPropertyName("PixelScale")]
    public double PixelScale { get; init; }

    [JsonPropertyName("Scale")]
    public int Scale { get; init; }
}

/// <summary>
/// RMS error statistics for guiding
/// </summary>
public record GuideRMS
{
    [JsonPropertyName("RA")]
    public double RA { get; init; }

    [JsonPropertyName("Dec")]
    public double Dec { get; init; }

    [JsonPropertyName("Total")]
    public double Total { get; init; }

    [JsonPropertyName("RAText")]
    public string RAText { get; init; } = string.Empty;

    [JsonPropertyName("DecText")]
    public string DecText { get; init; } = string.Empty;

    [JsonPropertyName("TotalText")]
    public string TotalText { get; init; } = string.Empty;

    [JsonPropertyName("PeakRAText")]
    public string PeakRAText { get; init; } = string.Empty;

    [JsonPropertyName("PeakDecText")]
    public string PeakDecText { get; init; } = string.Empty;

    [JsonPropertyName("Scale")]
    public double Scale { get; init; }

    [JsonPropertyName("PeakRA")]
    public double PeakRA { get; init; }

    [JsonPropertyName("PeakDec")]
    public double PeakDec { get; init; }

    [JsonPropertyName("DataPoints")]
    public int DataPoints { get; init; }
}

/// <summary>
/// Individual guide step with extended information for history display
/// </summary>
public record GuideStepHistory
{
    [JsonPropertyName("Id")]
    public int Id { get; init; }

    [JsonPropertyName("IdOffsetLeft")]
    public double IdOffsetLeft { get; init; }

    [JsonPropertyName("IdOffsetRight")]
    public double IdOffsetRight { get; init; }

    [JsonPropertyName("RADistanceRaw")]
    public double RADistanceRaw { get; init; }

    [JsonPropertyName("RADistanceRawDisplay")]
    public double RADistanceRawDisplay { get; init; }

    [JsonPropertyName("RADuration")]
    public int RADuration { get; init; }

    [JsonPropertyName("DECDistanceRaw")]
    public double DECDistanceRaw { get; init; }

    [JsonPropertyName("DECDistanceRawDisplay")]
    public double DECDistanceRawDisplay { get; init; }

    [JsonPropertyName("DECDuration")]
    public int DECDuration { get; init; }

    [JsonPropertyName("Dither")]
    public string Dither { get; init; } = string.Empty;
}

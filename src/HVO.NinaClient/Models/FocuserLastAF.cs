using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Focuser autofocus point information
/// </summary>
public record FocusPoint
{
    [JsonPropertyName("Position")]
    public int Position { get; init; }

    [JsonPropertyName("Value")]
    public double Value { get; init; }

    [JsonPropertyName("Error")]
    public int Error { get; init; }
}

/// <summary>
/// Focuser calculated focus point information
/// </summary>
public record CalculatedFocusPoint
{
    [JsonPropertyName("Position")]
    public double Position { get; init; }

    [JsonPropertyName("Value")]
    public double Value { get; init; }

    [JsonPropertyName("Error")]
    public int Error { get; init; }
}

/// <summary>
/// Focuser measure point information
/// </summary>
public record MeasurePoint
{
    [JsonPropertyName("Position")]
    public int Position { get; init; }

    [JsonPropertyName("Value")]
    public double Value { get; init; }

    [JsonPropertyName("Error")]
    public double Error { get; init; }
}

/// <summary>
/// Trend line intersection information
/// </summary>
public record TrendLineIntersection
{
    [JsonPropertyName("Position")]
    public int Position { get; init; }

    [JsonPropertyName("Value")]
    public double Value { get; init; }

    [JsonPropertyName("Error")]
    public int Error { get; init; }
}

/// <summary>
/// Hyperbolic minimum information
/// </summary>
public record HyperbolicMinimum
{
    [JsonPropertyName("Position")]
    public double Position { get; init; }

    [JsonPropertyName("Value")]
    public double Value { get; init; }

    [JsonPropertyName("Error")]
    public int Error { get; init; }
}

/// <summary>
/// Autofocus intersections information
/// </summary>
public record Intersections
{
    [JsonPropertyName("TrendLineIntersection")]
    public TrendLineIntersection TrendLineIntersection { get; init; } = new();

    [JsonPropertyName("HyperbolicMinimum")]
    public HyperbolicMinimum HyperbolicMinimum { get; init; } = new();
}

/// <summary>
/// Autofocus fittings information
/// </summary>
public record Fittings
{
    [JsonPropertyName("Quadratic")]
    public string Quadratic { get; init; } = "";

    [JsonPropertyName("Hyperbolic")]
    public string Hyperbolic { get; init; } = "";

    [JsonPropertyName("Gaussian")]
    public string Gaussian { get; init; } = "";

    [JsonPropertyName("LeftTrend")]
    public string LeftTrend { get; init; } = "";

    [JsonPropertyName("RightTrend")]
    public string RightTrend { get; init; } = "";
}

/// <summary>
/// R-squared values for autofocus fittings
/// </summary>
public record RSquares
{
    [JsonPropertyName("Quadratic")]
    public double Quadratic { get; init; }

    [JsonPropertyName("Hyperbolic")]
    public double Hyperbolic { get; init; }

    [JsonPropertyName("LeftTrend")]
    public double LeftTrend { get; init; }

    [JsonPropertyName("RightTrend")]
    public int RightTrend { get; init; }
}

/// <summary>
/// Backlash compensation information
/// </summary>
public record BacklashCompensation
{
    [JsonPropertyName("BacklashCompensationModel")]
    public string BacklashCompensationModel { get; init; } = "";

    [JsonPropertyName("BacklashIN")]
    public int BacklashIN { get; init; }

    [JsonPropertyName("BacklashOUT")]
    public int BacklashOUT { get; init; }
}

/// <summary>
/// Last autofocus result information
/// </summary>
public record FocuserLastAF
{
    [JsonPropertyName("Version")]
    public int Version { get; init; }

    [JsonPropertyName("Filter")]
    public string Filter { get; init; } = "";

    [JsonPropertyName("AutoFocuserName")]
    public string AutoFocuserName { get; init; } = "";

    [JsonPropertyName("StarDetectorName")]
    public string StarDetectorName { get; init; } = "";

    [JsonPropertyName("Timestamp")]
    public string Timestamp { get; init; } = "";

    [JsonPropertyName("Temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("Method")]
    public string Method { get; init; } = "";

    [JsonPropertyName("Fitting")]
    public string Fitting { get; init; } = "";

    [JsonPropertyName("InitialFocusPoint")]
    public FocusPoint InitialFocusPoint { get; init; } = new();

    [JsonPropertyName("CalculatedFocusPoint")]
    public CalculatedFocusPoint CalculatedFocusPoint { get; init; } = new();

    [JsonPropertyName("PreviousFocusPoint")]
    public CalculatedFocusPoint PreviousFocusPoint { get; init; } = new();

    [JsonPropertyName("MeasurePoints")]
    public List<MeasurePoint> MeasurePoints { get; init; } = [];

    [JsonPropertyName("Intersections")]
    public Intersections Intersections { get; init; } = new();

    [JsonPropertyName("Fittings")]
    public Fittings Fittings { get; init; } = new();

    [JsonPropertyName("RSquares")]
    public RSquares RSquares { get; init; } = new();

    [JsonPropertyName("BacklashCompensation")]
    public BacklashCompensation BacklashCompensation { get; init; } = new();

    [JsonPropertyName("Duration")]
    public string Duration { get; init; } = "";
}

namespace HVO.Weather;

/// <summary>Categorised dew risk levels for observatory optics.</summary>
public enum DewRiskLevel
{
    /// <summary>Dew spread &gt; 10°F — dewing very unlikely.</summary>
    Low,

    /// <summary>Dew spread 5–10°F — some risk; monitor conditions.</summary>
    Moderate,

    /// <summary>Dew spread 2–5°F — high risk; dew heaters recommended.</summary>
    High,

    /// <summary>Dew spread &lt; 2°F — dewing imminent; dew heaters essential.</summary>
    Critical
}

/// <summary>
/// Calculates dew risk for astronomical optics based on temperature and dew point.
/// </summary>
/// <remarks>
/// Dew forms on optics when their surface temperature falls to or below the dew point.
/// The spread between ambient temperature and dew point is the primary indicator.
/// Wind and sky conditions also matter but are not modelled here.
/// </remarks>
public static class DewRisk
{
    /// <summary>
    /// Returns the dew risk level for the given temperature and dew point.
    /// </summary>
    /// <param name="temperatureFahrenheit">Ambient outside temperature (°F).</param>
    /// <param name="dewPointFahrenheit">Dew point (°F).</param>
    public static DewRiskLevel Calculate(double temperatureFahrenheit, double dewPointFahrenheit)
    {
        double spread = temperatureFahrenheit - dewPointFahrenheit;
        return spread switch
        {
            < 2  => DewRiskLevel.Critical,
            < 5  => DewRiskLevel.High,
            < 10 => DewRiskLevel.Moderate,
            _    => DewRiskLevel.Low
        };
    }

    /// <summary>
    /// Returns the dew risk level using Celsius values.
    /// </summary>
    /// <param name="temperatureCelsius">Ambient outside temperature (°C).</param>
    /// <param name="dewPointCelsius">Dew point (°C).</param>
    public static DewRiskLevel CalculateCelsius(double temperatureCelsius, double dewPointCelsius) =>
        Calculate(CelsiusToFahrenheit(temperatureCelsius), CelsiusToFahrenheit(dewPointCelsius));

    private static double CelsiusToFahrenheit(double c) => c * 9.0 / 5.0 + 32.0;
}

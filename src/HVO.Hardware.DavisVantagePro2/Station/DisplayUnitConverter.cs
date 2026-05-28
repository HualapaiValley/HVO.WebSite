namespace HVO.Hardware.DavisVantagePro2.Station;

public static class DisplayUnitConverter
{
    public static double? Temperature(double? fahrenheit, string? units) => units switch
    {
        "°C" or "°C×10" => fahrenheit.HasValue ? (fahrenheit.Value - 32.0) * 5.0 / 9.0 : null,
        _ => fahrenheit
    };

    public static double? Pressure(double? inHg, string? units) => units switch
    {
        "mmHg" => inHg * 25.4,
        "hPa" or "mbar" => inHg * 33.8638866667,
        _ => inHg
    };

    public static double? Rain(double? inches, string? units) => units switch
    {
        "mm" => inches * 25.4,
        _ => inches
    };

    public static double? WindSpeed(double? mph, string? units) => units switch
    {
        "m/s" => mph * 0.44704,
        "km/h" => mph * 1.609344,
        "knots" => mph * 0.8689762419,
        _ => mph
    };

    public static string TemperatureSuffix(string? units) => units switch
    {
        "°C" or "°C×10" => "°C",
        _ => "°F"
    };

    public static string PressureSuffix(string? units) => units switch
    {
        "mmHg" => "mmHg",
        "hPa" => "hPa",
        "mbar" => "mbar",
        _ => "inHg"
    };

    public static string RainSuffix(string? units) => units == "mm" ? "mm" : "in";

    public static string RainRateSuffix(string? units) => units == "mm" ? "mm/hr" : "in/hr";

    public static string WindSuffix(string? units) => units switch
    {
        "m/s" => "m/s",
        "km/h" => "km/h",
        "knots" => "knots",
        _ => "mph"
    };
}

using System.Globalization;

namespace HVO.WebSite.Themes.Components.Format;

public enum UnitSystem { Metric, Imperial }

public static class HvoFormat
{
    public static string Timestamp(DateTime? utc, string? format = null)
        => utc?.ToLocalTime().ToString(format ?? "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";

    public static string FooterTimestamp(DateTime? utc)
        => Timestamp(utc, "dd MMM yyyy - h:mm:ss tt");

    public static string Temperature(double? celsius, UnitSystem units = UnitSystem.Metric)
        => celsius is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{celsius.Value * 9.0 / 5.0 + 32.0:F1} °F"
                : $"{celsius.Value:F1} °C";

    public static string Speed(double? ms, UnitSystem units = UnitSystem.Metric)
        => ms is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{ms.Value * 2.23694:F1} mph"
                : $"{ms.Value * 3.6:F1} km/h";

    public static string PressureInHg(double? inhg, UnitSystem units = UnitSystem.Metric)
        => inhg is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{inhg.Value:F2} inHg"
                : $"{inhg.Value * 33.8639:F1} hPa";

    public static string Voltage(double? volts, int decimals = 2)
        => volts is null ? "--" : $"{volts.Value.ToString($"F{decimals}")} V";

    public static string Current(double? amps)
        => amps is null ? "--" : $"{amps.Value:F1} A";

    public static string Power(double? watts)
        => watts is null ? "--" : $"{watts.Value:F0} W";

    public static string EnergyAh(double? ah)
        => ah is null ? "--" : $"{ah.Value:F1} Ah";

    public static string Rain(double? inches)
        => inches is null ? "--" : $"{inches.Value:F2} in";

    public static string Percent(double? value)
        => value is null ? "--" : $"{value.Value:F0} %";

    public static string Duration(TimeSpan? ts)
        => ts is null ? "--" : ts.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}

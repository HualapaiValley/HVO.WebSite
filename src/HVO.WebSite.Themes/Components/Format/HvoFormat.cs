using System.Globalization;

namespace HVO.WebSite.Themes.Components.Format;

public enum UnitSystem { Metric, Imperial }

public static class HvoFormat
{
    public static string Timestamp(DateTime? utc, string? format = null)
        => utc?.ToLocalTime().ToString(format ?? "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";

    public static string Timestamp(DateTime? utc, TimeZoneInfo timeZone, string? format = null)
        => utc.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), timeZone)
                .ToString(format ?? "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "--";

    public static string FooterTimestamp(DateTime? utc)
        => Timestamp(utc, "dd MMM yyyy - h:mm:ss tt");

    public static string FooterTimestamp(DateTime? utc, TimeZoneInfo timeZone)
        => Timestamp(utc, timeZone, "dd MMM yyyy - h:mm:ss tt");

    public static string Temperature(double? celsius, UnitSystem units = UnitSystem.Metric)
        => celsius is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{(celsius.Value * 9.0 / 5.0 + 32.0).ToString("F1", CultureInfo.InvariantCulture)} °F"
                : $"{celsius.Value.ToString("F1", CultureInfo.InvariantCulture)} °C";

    public static string TemperatureBoth(double? celsius)
        => celsius is null
            ? "--"
            : $"{celsius.Value.ToString("F1", CultureInfo.InvariantCulture)} °C / {(celsius.Value * 9.0 / 5.0 + 32.0).ToString("F1", CultureInfo.InvariantCulture)} °F";

    public static string Speed(double? ms, UnitSystem units = UnitSystem.Metric)
        => ms is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{(ms.Value * 2.23694).ToString("F1", CultureInfo.InvariantCulture)} mph"
                : $"{(ms.Value * 3.6).ToString("F1", CultureInfo.InvariantCulture)} km/h";

    public static string PressureInHg(double? inhg, UnitSystem units = UnitSystem.Metric)
        => inhg is null ? "--"
            : units == UnitSystem.Imperial
                ? $"{inhg.Value.ToString("F2", CultureInfo.InvariantCulture)} inHg"
                : $"{(inhg.Value * 33.8639).ToString("F1", CultureInfo.InvariantCulture)} hPa";

    public static string Voltage(double? volts, int decimals = 2)
        => volts is null ? "--" : $"{volts.Value.ToString($"F{decimals}", CultureInfo.InvariantCulture)} V";

    public static string Current(double? amps)
        => amps is null ? "--" : $"{amps.Value.ToString("F1", CultureInfo.InvariantCulture)} A";

    public static string SignedCurrent(double? amps)
        => amps is null ? "--" : $"{amps.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)} A";

    public static string Power(double? watts)
        => watts is null ? "--" : $"{watts.Value.ToString("F0", CultureInfo.InvariantCulture)} W";

    public static string SignedPower(double? watts)
        => watts is null ? "--" : $"{watts.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture)} W";

    public static string EnergyAh(double? ah)
        => ah is null ? "--" : $"{ah.Value.ToString("F1", CultureInfo.InvariantCulture)} Ah";

    public static string Rain(double? inches)
        => inches is null ? "--" : $"{inches.Value.ToString("F2", CultureInfo.InvariantCulture)} in";

    public static string Percent(double? value)
        => value is null ? "--" : $"{value.Value.ToString("F0", CultureInfo.InvariantCulture)} %";

    public static string Duration(TimeSpan? ts)
        => ts is null ? "--" : ts.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    public static string Integer(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "--";
}

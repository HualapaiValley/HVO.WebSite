using System.Globalization;

namespace HVO.Hardware.JkBms.Components.Pages;

public static class BmsDisplayFormatting
{
    public static double IntoPackCurrentAmps(int rawCurrentMa) => rawCurrentMa / 1000d;

    public static double IntoPackPowerWatts(uint packVoltageMv, int rawCurrentMa) =>
        packVoltageMv / 1000d * rawCurrentMa / 1000d;

    public static string CurrentLabel(int rawCurrentMa) =>
        CurrentLabel(IntoPackCurrentAmps(rawCurrentMa));

    public static string CurrentLabel(double? intoPackCurrentAmps) => intoPackCurrentAmps.HasValue
        ? $"{intoPackCurrentAmps.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)} A"
        : "--";

    public static string PowerLabel(double? intoPackPowerWatts) => intoPackPowerWatts.HasValue
        ? $"{intoPackPowerWatts.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture)} W"
        : "--";

    public static string FlowLabel(double intoPackPowerWatts) => intoPackPowerWatts switch
    {
        > 0.5 => "Into pack",
        < -0.5 => "Out of pack",
        _ => "Idle",
    };
}
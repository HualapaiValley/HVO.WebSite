using HVO.Gateway.TplinkKasa.Hosting;
using System.Globalization;

namespace HVO.Gateway.TplinkKasa.Components;

public static class KasaCardText
{
    public static string FormatState(KasaDeviceStatus device) =>
        device.IsOn is null ? "Unknown" : device.IsOn.Value ? "On" : "Off";

    public static string FormatPower(KasaDeviceStatus device) => FormatPower(device.Energy);

    public static string FormatPower(KasaEnergyStatus? energy) =>
        energy?.PowerW is null ? "n/a" : $"{energy.PowerW.Value:0.#} W";

    public static string FormatEnergy(KasaEnergyStatus? energy) =>
        energy?.EnergyKWh is null ? "n/a" : $"{energy.EnergyKWh.Value:0.###} kWh";

    public static string FormatOutletEnergy(KasaEnergyStatus energy) =>
        energy.EnergyKWh is null
            ? FormatPower(energy)
            : $"{FormatPower(energy)} / {FormatEnergy(energy)}";

    public static string FormatPercent(int? value) => value is null ? "Unknown" : $"{value.Value.ToString(CultureInfo.InvariantCulture)}%";

    public static string FormatKelvin(int? value) => value is null || value == 0 ? "n/a" : $"{value.Value.ToString(CultureInfo.InvariantCulture)} K";

    public static string FormatHueSaturation(KasaLightStatus light) => FormatHueSaturation(light.Hue, light.Saturation);

    public static string FormatHueSaturation(int? hue, int? saturation) =>
        hue is null && saturation is null
            ? "n/a"
            : $"H {hue?.ToString(CultureInfo.InvariantCulture) ?? "?"} / S {saturation?.ToString(CultureInfo.InvariantCulture) ?? "?"}";

    public static string FormatOutletName(KasaOutletStatus outlet) =>
        string.IsNullOrWhiteSpace(outlet.DisplayName)
            ? $"Outlet {outlet.Index?.ToString(CultureInfo.InvariantCulture) ?? "?"}"
            : outlet.DisplayName;

    public static string FormatOutletId(KasaOutletStatus outlet) =>
        outlet.Index is null ? "Port unknown" : $"Port {outlet.Index.Value.ToString(CultureInfo.InvariantCulture)}";

    public static string FormatOutletState(KasaOutletStatus outlet) =>
        outlet.IsOn is null ? "Unknown" : outlet.IsOn.Value ? "On" : "Off";

    public static string FormatOutletOnTime(KasaOutletStatus outlet) =>
        outlet.OnTimeSeconds is null ? "Unknown" : TimeSpan.FromSeconds(outlet.OnTimeSeconds.Value).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    public static string FormatNullable(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";

    public static string FormatMilliseconds(int? value) => value is null ? "Unknown" : $"{value.Value.ToString(CultureInfo.InvariantCulture)} ms";
}

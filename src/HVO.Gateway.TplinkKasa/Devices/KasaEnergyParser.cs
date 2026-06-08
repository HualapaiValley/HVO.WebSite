using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaEnergyParser
{
    public KasaEnergyReading? Parse(JsonDocument response) => Parse(response, "emeter", "get_realtime");

    public KasaEnergyReading? Parse(JsonDocument response, params string[] path)
    {
        if (!TryGetNested(response.RootElement, path, out var realtime)
            || realtime.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (GetInt(realtime, "err_code") is int errCode && errCode != 0)
        {
            return null;
        }

        var powerW = GetDouble(realtime, "power_mw") is double powerMw
            ? powerMw / 1000.0
            : GetDouble(realtime, "power");
        var voltageV = GetDouble(realtime, "voltage_mv") is double voltageMv
            ? voltageMv / 1000.0
            : GetDouble(realtime, "voltage");
        var currentA = GetDouble(realtime, "current_ma") is double currentMa
            ? currentMa / 1000.0
            : GetDouble(realtime, "current");
        var energyKWh = GetDouble(realtime, "total_wh") is double totalWh
            ? totalWh / 1000.0
            : GetDouble(realtime, "total");

        if (powerW is null && voltageV is null && currentA is null && energyKWh is null)
        {
            return null;
        }

        return new KasaEnergyReading(powerW, voltageV, currentA, energyKWh, realtime.Clone());
    }

    private static bool TryGetNested(JsonElement root, string[] path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && KasaJson.TryGetInt(property, out var value)
            ? value
            : null;

    private static double? GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && KasaJson.TryGetDouble(property, out var value)
            ? value
            : null;
}

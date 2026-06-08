using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaSystemInfoParser
{
    public KasaSystemInfo Parse(JsonDocument response)
    {
        if (!TryGetNested(response.RootElement, ["system", "get_sysinfo"], out var sysinfo)
            || sysinfo.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Kasa response did not contain system.get_sysinfo object.");
        }

        var children = new List<KasaChildInfo>();
        if (sysinfo.TryGetProperty("children", out var childrenElement)
            && childrenElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in childrenElement.EnumerateArray())
            {
                children.Add(new KasaChildInfo(
                    GetString(child, "id"),
                    GetString(child, "alias"),
                    GetInt(child, "state"),
                    GetInt(child, "on_time")));
            }
        }

        KasaLightState? lightState = null;
        if (sysinfo.TryGetProperty("light_state", out var lightElement)
            && lightElement.ValueKind == JsonValueKind.Object)
        {
            lightState = new KasaLightState(
                GetInt(lightElement, "on_off"),
                GetInt(lightElement, "hue"),
                GetInt(lightElement, "saturation"),
                GetInt(lightElement, "color_temp"),
                GetInt(lightElement, "brightness"),
                GetString(lightElement, "mode"),
                lightElement.Clone());
        }

        var preferredStates = new List<KasaPreferredLightState>();
        if (sysinfo.TryGetProperty("preferred_state", out var preferredElement)
            && preferredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var preferredState in preferredElement.EnumerateArray())
            {
                preferredStates.Add(new KasaPreferredLightState(
                    GetInt(preferredState, "index"),
                    GetInt(preferredState, "brightness"),
                    GetInt(preferredState, "hue"),
                    GetInt(preferredState, "saturation"),
                    GetInt(preferredState, "color_temp")));
            }
        }

        var latitudeRaw = GetInt(sysinfo, "latitude_i");
        var longitudeRaw = GetInt(sysinfo, "longitude_i");

        return new KasaSystemInfo(
            GetString(sysinfo, "deviceId"),
            GetString(sysinfo, "alias"),
            GetString(sysinfo, "model"),
            GetString(sysinfo, "type"),
            GetString(sysinfo, "hw_ver"),
            GetString(sysinfo, "sw_ver"),
            GetString(sysinfo, "mac") ?? GetString(sysinfo, "mic_mac"),
            GetString(sysinfo, "hwId"),
            GetString(sysinfo, "fwId"),
            GetString(sysinfo, "oemId"),
            GetString(sysinfo, "feature"),
            GetString(sysinfo, "active_mode"),
            GetInt(sysinfo, "rssi"),
            latitudeRaw is null && longitudeRaw is null
                ? null
                : new KasaDeviceLocation(latitudeRaw, longitudeRaw, NormalizeCoordinate(latitudeRaw, 90), NormalizeCoordinate(longitudeRaw, 180)),
            GetInt(sysinfo, "relay_state"),
            GetInt(sysinfo, "on_time"),
            children,
            lightState,
                preferredStates,
            sysinfo.Clone());
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

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && KasaJson.TryGetInt(property, out var value)
            ? value
            : null;

    private static double? NormalizeCoordinate(int? value, int limit)
    {
        if (value is null)
        {
            return null;
        }

        var degrees = value.Value / 10000d;
        return Math.Abs(degrees) <= limit ? degrees : null;
    }
}

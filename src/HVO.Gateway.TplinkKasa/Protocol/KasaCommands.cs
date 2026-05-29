using System.Globalization;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Protocol;

public static class KasaCommands
{
    public const string GetSystemInfo = "{\"system\":{\"get_sysinfo\":{}}}";
    public const string GetDeviceIcon = "{\"system\":{\"get_dev_icon\":{}}}";
    public const string GetDownloadState = "{\"system\":{\"get_download_state\":{}}}";
    public const string GetRealtimeEnergy = "{\"emeter\":{\"get_realtime\":{}}}";
    public const string GetEnergyGain = "{\"emeter\":{\"get_vgain_igain\":{}}}";
    public const string GetScheduleRules = "{\"schedule\":{\"get_rules\":{}}}";
    public const string GetNextScheduleAction = "{\"schedule\":{\"get_next_action\":{}}}";
    public const string GetCountdownRules = "{\"count_down\":{\"get_rules\":{}}}";
    public const string GetAwayRules = "{\"anti_theft\":{\"get_rules\":{}}}";
    public const string GetLedState = "{\"system\":{\"get_led_off\":{}}}";
    public const string GetCloudInfo = "{\"cnCloud\":{\"get_info\":{}}}";
    public const string GetCloudFirmwareList = "{\"cnCloud\":{\"get_intl_fw_list\":{}}}";
    public const string GetTime = "{\"time\":{\"get_time\":{}}}";
    public const string GetTimezone = "{\"time\":{\"get_timezone\":{}}}";
    public const string GetBulbLightState = "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_light_state\":{}}}";
    public const string GetBulbLightDetails = "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_light_details\":{}}}";
    public const string GetBulbCloudInfo = "{\"smartlife.iot.common.cloud\":{\"get_info\":{}}}";
    public const string GetBulbTime = "{\"smartlife.iot.common.timesetting\":{\"get_time\":{}}}";
    public const string GetBulbTimezone = "{\"smartlife.iot.common.timesetting\":{\"get_timezone\":{}}}";
    public const string GetBulbScheduleRules = "{\"smartlife.iot.common.schedule\":{\"get_rules\":{}}}";
    public const string GetBulbNextScheduleAction = "{\"smartlife.iot.common.schedule\":{\"get_next_action\":{}}}";
    public const string GetBulbRealtimeEnergy = "{\"smartlife.iot.common.emeter\":{\"get_realtime\":{}}}";
    public const string GetDimmerDefaultBehavior = "{\"smartlife.iot.dimmer\":{\"get_default_behavior\":{}}}";
    public const string GetDimmerParameters = "{\"smartlife.iot.dimmer\":{\"get_dimmer_parameters\":{}}}";
    public const string GetCachedWifiScanInfo = "{\"netif\":{\"get_scaninfo\":{\"refresh\":0}}}";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> ReadOnlyCommands = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
    {
        ["system"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_sysinfo",
            "get_dev_icon",
            "get_download_state",
            "get_led_off"
        },
        ["emeter"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_realtime",
            "get_vgain_igain",
            "get_daystat",
            "get_monthstat"
        },
        ["schedule"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_rules",
            "get_next_action"
        },
        ["count_down"] = new HashSet<string>(StringComparer.Ordinal) { "get_rules" },
        ["anti_theft"] = new HashSet<string>(StringComparer.Ordinal) { "get_rules" },
        ["cnCloud"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_info",
            "get_intl_fw_list"
        },
        ["time"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_time",
            "get_timezone"
        },
        ["smartlife.iot.smartbulb.lightingservice"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_light_state",
            "get_light_details"
        },
        ["smartlife.iot.common.cloud"] = new HashSet<string>(StringComparer.Ordinal) { "get_info" },
        ["smartlife.iot.common.timesetting"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_time",
            "get_timezone"
        },
        ["smartlife.iot.common.schedule"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_rules",
            "get_next_action"
        },
        ["smartlife.iot.common.emeter"] = new HashSet<string>(StringComparer.Ordinal) { "get_realtime" },
        ["smartlife.iot.dimmer"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_default_behavior",
            "get_dimmer_parameters"
        },
        ["netif"] = new HashSet<string>(StringComparer.Ordinal) { "get_scaninfo" }
    };

    public static string GetEnergyDayStats(int year, int month) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"emeter\":{{\"get_daystat\":{{\"year\":{year},\"month\":{month}}}}}}}");

    public static string GetEnergyMonthStats(int year) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"emeter\":{{\"get_monthstat\":{{\"year\":{year}}}}}}}");

    public static bool IsKnownReadOnly(JsonDocument command)
    {
        var root = command.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var sawCommand = false;
        foreach (var module in root.EnumerateObject())
        {
            if (module.Value.ValueKind != JsonValueKind.Object
                || !ReadOnlyCommands.TryGetValue(module.Name, out var allowedCommands))
            {
                return false;
            }

            foreach (var moduleCommand in module.Value.EnumerateObject())
            {
                sawCommand = true;
                if (!allowedCommands.Contains(moduleCommand.Name)
                    || !HasSafeReadOnlyParameters(module.Name, moduleCommand.Name, moduleCommand.Value))
                {
                    return false;
                }
            }
        }

        return sawCommand;
    }

    private static bool HasSafeReadOnlyParameters(string module, string command, JsonElement parameters)
    {
        if (module == "emeter" && command == "get_daystat")
        {
            return HasOnlyIntegerProperties(parameters, "year", "month")
                && TryGetIntProperty(parameters, "month", out var month)
                && month is >= 1 and <= 12;
        }

        if (module == "emeter" && command == "get_monthstat")
        {
            return HasOnlyIntegerProperties(parameters, "year");
        }

        if (module == "netif" && command == "get_scaninfo")
        {
            return HasOnlyCachedWifiScanParameters(parameters);
        }

        return IsEmptyObjectOrNull(parameters);
    }

    private static bool IsEmptyObjectOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
        || (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any());

    private static bool HasOnlyIntegerProperties(JsonElement value, params string[] expectedProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!expectedProperties.Contains(property.Name) || !TryGetInt(property.Value, out _))
            {
                return false;
            }

            seen.Add(property.Name);
        }

        return seen.SetEquals(expectedProperties);
    }

    private static bool HasOnlyCachedWifiScanParameters(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var sawRefresh = false;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name != "refresh")
            {
                return false;
            }

            sawRefresh = true;
            if (property.Value.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            if (!TryGetInt(property.Value, out var refresh) || refresh != 0)
            {
                return false;
            }
        }

        return sawRefresh;
    }

    private static bool TryGetIntProperty(JsonElement value, string propertyName, out int result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(propertyName, out var property)
            && TryGetInt(property, out result);
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        value = default;
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        return element.ValueKind == JsonValueKind.String
            && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

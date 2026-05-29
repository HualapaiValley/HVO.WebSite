using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Protocol;

public static class KasaCommands
{
    public const string GetSystemInfo = "{\"system\":{\"get_sysinfo\":{}}}";
    public const string GetRealtimeEnergy = "{\"emeter\":{\"get_realtime\":{}}}";
    public const string GetScheduleRules = "{\"schedule\":{\"get_rules\":{}}}";
    public const string GetNextScheduleAction = "{\"schedule\":{\"get_next_action\":{}}}";
    public const string GetCountdownRules = "{\"count_down\":{\"get_rules\":{}}}";
    public const string GetAwayRules = "{\"anti_theft\":{\"get_rules\":{}}}";
    public const string GetLedState = "{\"system\":{\"get_led_off\":{}}}";
    public const string GetCloudInfo = "{\"cnCloud\":{\"get_info\":{}}}";
    public const string GetTime = "{\"time\":{\"get_time\":{}}}";
    public const string GetTimezone = "{\"time\":{\"get_timezone\":{}}}";

    public static string GetEnergyDayStats(int year, int month) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"emeter\":{{\"get_daystat\":{{\"year\":{year},\"month\":{month}}}}}}}");

    public static string GetEnergyMonthStats(int year) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"emeter\":{{\"get_monthstat\":{{\"year\":{year}}}}}}}");

    public static bool IsKnownReadOnly(JsonDocument command)
    {
        var root = command.RootElement;
        return HasModuleCommand(root, "system", "get_sysinfo")
            || HasModuleCommand(root, "emeter", "get_realtime")
            || HasModuleCommand(root, "emeter", "get_daystat")
            || HasModuleCommand(root, "emeter", "get_monthstat")
            || HasModuleCommand(root, "schedule", "get_rules")
            || HasModuleCommand(root, "schedule", "get_next_action")
            || HasModuleCommand(root, "count_down", "get_rules")
            || HasModuleCommand(root, "anti_theft", "get_rules")
            || HasModuleCommand(root, "system", "get_led_off")
            || HasModuleCommand(root, "cnCloud", "get_info")
            || HasModuleCommand(root, "time", "get_time")
            || HasModuleCommand(root, "time", "get_timezone");
    }

    private static bool HasModuleCommand(JsonElement root, string module, string command) =>
        root.TryGetProperty(module, out var moduleElement)
        && moduleElement.ValueKind == JsonValueKind.Object
        && moduleElement.TryGetProperty(command, out _);
}

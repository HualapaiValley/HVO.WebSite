using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Protocol;

public static class KasaCommands
{
    public const string GetSystemInfo = "{\"system\":{\"get_sysinfo\":{}}}";
    public const string GetRealtimeEnergy = "{\"emeter\":{\"get_realtime\":{}}}";
    public const string GetScheduleRules = "{\"schedule\":{\"get_rules\":{}}}";
    public const string GetCountdownRules = "{\"count_down\":{\"get_rules\":{}}}";
    public const string GetAwayRules = "{\"anti_theft\":{\"get_rules\":{}}}";
    public const string GetLedState = "{\"system\":{\"get_led_off\":{}}}";

    public static bool IsKnownReadOnly(JsonDocument command)
    {
        var root = command.RootElement;
        return HasModuleCommand(root, "system", "get_sysinfo")
            || HasModuleCommand(root, "emeter", "get_realtime")
            || HasModuleCommand(root, "schedule", "get_rules")
            || HasModuleCommand(root, "count_down", "get_rules")
            || HasModuleCommand(root, "anti_theft", "get_rules")
            || HasModuleCommand(root, "system", "get_led_off");
    }

    private static bool HasModuleCommand(JsonElement root, string module, string command) =>
        root.TryGetProperty(module, out var moduleElement)
        && moduleElement.ValueKind == JsonValueKind.Object
        && moduleElement.TryGetProperty(command, out _);
}

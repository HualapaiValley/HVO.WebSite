using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Protocol;

public sealed class KasaLegacyLabClient(TimeSpan timeout)
{
    private const int MaxPayloadBytes = 1024 * 1024;
    private const string LabRuleIdPrefix = "hvo-plug-lab-";

    public Task<JsonDocument> SendAsync(string host, int port, string commandJson, CancellationToken cancellationToken) =>
        SendCoreAsync(host, port, commandJson, cancellationToken);

    private async Task<JsonDocument> SendCoreAsync(string host, int port, string commandJson, CancellationToken cancellationToken)
    {
        using var command = JsonDocument.Parse(commandJson);
        ValidateLabCommand(command);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);

        await using var stream = tcpClient.GetStream();
        var frame = KasaFrameCodec.EncodeTcpFrame(commandJson);
        await stream.WriteAsync(frame, timeoutCts.Token).ConfigureAwait(false);

        var lengthBuffer = new byte[4];
        await ReadExactlyAsync(stream, lengthBuffer, timeoutCts.Token).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (payloadLength <= 0 || payloadLength > MaxPayloadBytes)
        {
            throw new InvalidDataException($"Invalid Kasa TCP payload length: {payloadLength}.");
        }

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, timeoutCts.Token).ConfigureAwait(false);
        var json = KasaFrameCodec.DecodeTcpPayload(payload);
        return JsonDocument.Parse(json);
    }

    private static void ValidateLabCommand(JsonDocument command)
    {
        var root = command.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Plug lab commands must be JSON objects.");
        }

        var modules = root.EnumerateObject().Where(property => property.Name != "context").ToArray();
        if (modules.Length != 1 || !HasValidContext(root))
        {
            throw new InvalidOperationException("Plug lab commands must contain exactly one module and an optional safe child context.");
        }

        var module = modules.Single();
        if (module.Value.ValueKind != JsonValueKind.Object
            || module.Value.EnumerateObject().Count() != 1)
        {
            throw new InvalidOperationException("Plug lab commands must contain exactly one module command.");
        }

        var moduleCommand = module.Value.EnumerateObject().Single();
        if (IsAllowedCommand(module.Name, moduleCommand.Name, moduleCommand.Value))
        {
            return;
        }

        throw new InvalidOperationException("Plug lab command is not in the guarded Desk Lamp allowlist.");
    }

    private static bool IsAllowedCommand(string module, string command, JsonElement value) =>
        module switch
        {
            "system" => IsAllowedSystemCommand(command, value),
            "time" => IsAllowedTimeCommand(command, value),
            "emeter" => IsAllowedEmeterCommand(command, value),
            "schedule" => IsAllowedScheduleCommand(command, value),
            "count_down" => IsAllowedCountdownCommand(command, value),
            "anti_theft" => IsAllowedAntiTheftCommand(command, value),
            "cnCloud" => IsAllowedCloudCommand(command, value),
            "smartlife.iot.dimmer" => IsAllowedDimmerCommand(command, value),
            "smartlife.iot.smartbulb.lightingservice" => IsAllowedBulbLightingCommand(command, value),
            _ => false
        };

    private static bool HasValidContext(JsonElement root)
    {
        if (!root.TryGetProperty("context", out var context))
        {
            return true;
        }

        if (context.ValueKind != JsonValueKind.Object
            || context.EnumerateObject().Count() != 1
            || !context.TryGetProperty("child_ids", out var childIds)
            || childIds.ValueKind != JsonValueKind.Array
            || childIds.GetArrayLength() != 1)
        {
            return false;
        }

        var childId = childIds[0];
        return childId.ValueKind == JsonValueKind.String
            && childId.GetString() is { Length: > 0 and <= 128 };
    }

    private static bool IsAllowedBulbLightingCommand(string command, JsonElement value) =>
        command switch
        {
            "transition_light_state" => HasValidLightTransition(value),
            _ => false
        };

    private static bool HasValidLightTransition(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !HasOnlyProperties(value, ["ignore_default", "on_off", "brightness", "hue", "saturation", "color_temp", "transition_period"]))
        {
            return false;
        }

        return TryGetIntProperty(value, "ignore_default", out var ignoreDefault)
            && ignoreDefault is 0 or 1
            && TryGetIntProperty(value, "on_off", out var onOff)
            && onOff is 0 or 1
            && TryGetIntProperty(value, "brightness", out var brightness)
            && brightness is >= 1 and <= 100
            && TryGetIntProperty(value, "hue", out var hue)
            && hue is >= 0 and <= 360
            && TryGetIntProperty(value, "saturation", out var saturation)
            && saturation is >= 0 and <= 100
            && TryGetIntProperty(value, "color_temp", out var colorTemperature)
            && colorTemperature is >= 0 and <= 9000
            && TryGetIntProperty(value, "transition_period", out var transitionPeriod)
            && transitionPeriod is >= 0 and <= 5000;
    }

    private static bool IsAllowedDimmerCommand(string command, JsonElement value) =>
        command switch
        {
            "get_default_behavior" => IsEmptyObjectOrNull(value),
            "get_dimmer_parameters" => IsEmptyObjectOrNull(value),
            "set_brightness" => HasOnlyIntegerProperties(value, ["brightness"])
                && TryGetIntProperty(value, "brightness", out var brightness)
                && brightness is >= 1 and <= 100,
            _ => false
        };

    private static bool IsAllowedSystemCommand(string command, JsonElement value) =>
        command switch
        {
            "get_sysinfo" => IsEmptyObjectOrNull(value),
            "get_dev_icon" => IsEmptyObjectOrNull(value),
            "get_download_state" => IsEmptyObjectOrNull(value),
            "get_led_off" => IsEmptyObjectOrNull(value),
            "set_relay_state" => HasOnlyIntegerProperties(value, ["state"])
                && TryGetIntProperty(value, "state", out var state)
                && state is 0 or 1,
            "set_dev_alias" => value.ValueKind == JsonValueKind.Object
                && value.EnumerateObject().Count() == 1
                && value.TryGetProperty("alias", out var alias)
                && alias.ValueKind == JsonValueKind.String
                && alias.GetString() is { Length: > 0 and <= 64 },
            "set_led_off" => HasOnlyIntegerProperties(value, ["off"])
                && TryGetIntProperty(value, "off", out var off)
                && off is 0 or 1,
            "reboot" => HasOnlyIntegerProperties(value, ["delay"])
                && TryGetIntProperty(value, "delay", out var delay)
                && delay is >= 1 and <= 30,
            _ => false
        };

    private static bool IsAllowedCloudCommand(string command, JsonElement value) =>
        command switch
        {
            "get_info" => IsEmptyObjectOrNull(value),
            "get_intl_fw_list" => IsEmptyObjectOrNull(value),
            _ => false
        };

    private static bool IsAllowedTimeCommand(string command, JsonElement value) =>
        command switch
        {
            "get_time" => IsEmptyObjectOrNull(value),
            "get_timezone" => IsEmptyObjectOrNull(value),
            "set_time" => HasValidDateTimeParameters(value, requireIndex: false),
            "set_timezone" => HasValidDateTimeParameters(value, requireIndex: true),
            _ => false
        };

    private static bool IsAllowedEmeterCommand(string command, JsonElement value) =>
        command switch
        {
            "get_realtime" => IsEmptyObjectOrNull(value),
            "get_daystat" => HasOnlyIntegerProperties(value, ["year", "month"])
                && TryGetIntProperty(value, "month", out var month)
                && month is >= 1 and <= 12,
            "get_monthstat" => HasOnlyIntegerProperties(value, ["year"]),
            "erase_emeter_stat" => IsEmptyObjectOrNull(value),
            "erase_runtime_stat" => IsEmptyObjectOrNull(value),
            _ => false
        };

    private static bool IsAllowedReadOnlyRuleCommand(string command, JsonElement value) =>
        command switch
        {
            "get_rules" => IsEmptyObjectOrNull(value),
            "get_next_action" => IsEmptyObjectOrNull(value),
            _ => false
        };

    private static bool IsAllowedAntiTheftCommand(string command, JsonElement value) =>
        command switch
        {
            "get_rules" => IsEmptyObjectOrNull(value),
            "get_next_action" => IsEmptyObjectOrNull(value),
            "add_rule" => HasValidLabScheduleRule(value),
            "delete_rule" => value.ValueKind == JsonValueKind.Object
                && value.EnumerateObject().Count() == 1
                && value.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { } idText
                && IsAllowedLabRuleId(idText),
            _ => false
        };

    private static bool IsAllowedScheduleCommand(string command, JsonElement value) =>
        command switch
        {
            "get_rules" => IsEmptyObjectOrNull(value),
            "get_next_action" => IsEmptyObjectOrNull(value),
            "get_daystat" => HasOnlyIntegerProperties(value, ["year", "month"])
                && TryGetIntProperty(value, "month", out var month)
                && month is >= 1 and <= 12,
            "get_monthstat" => HasOnlyIntegerProperties(value, ["year"]),
            "set_overall_enable" => HasOnlyIntegerProperties(value, ["enable"])
                && TryGetIntProperty(value, "enable", out var enable)
                && enable is 0 or 1,
            "add_rule" => HasValidLabScheduleRule(value),
            "delete_rule" => value.ValueKind == JsonValueKind.Object
                && value.EnumerateObject().Count() == 1
                && value.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { } idText
                && IsAllowedLabRuleId(idText),
            _ => false
        };

    private static bool IsAllowedCountdownCommand(string command, JsonElement value) =>
        command switch
        {
            "get_rules" => IsEmptyObjectOrNull(value),
            "get_next_action" => IsEmptyObjectOrNull(value),
            "add_rule" => HasValidLabCountdownRule(value),
            "edit_rule" => HasValidLabCountdownEditRule(value),
            "delete_rule" => value.ValueKind == JsonValueKind.Object
                && value.EnumerateObject().Count() == 1
                && value.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { } idText
                && IsAllowedLabRuleId(idText),
            _ => false
        };

    private static bool IsEmptyObjectOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
        || (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any());

    private static bool HasValidDateTimeParameters(JsonElement value, bool requireIndex)
    {
        var expected = requireIndex
            ? new[] { "year", "month", "mday", "hour", "min", "sec", "index" }
            : ["year", "month", "mday", "hour", "min", "sec"];
        return HasOnlyIntegerProperties(value, expected)
            && TryGetIntProperty(value, "year", out var year)
            && TryGetIntProperty(value, "month", out var month)
            && TryGetIntProperty(value, "mday", out var day)
            && TryGetIntProperty(value, "hour", out var hour)
            && TryGetIntProperty(value, "min", out var minute)
            && TryGetIntProperty(value, "sec", out var second)
            && year is >= 2020 and <= 2100
            && month is >= 1 and <= 12
            && day is >= 1 and <= 31
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59
            && second is >= 0 and <= 59
            && (!requireIndex || (TryGetIntProperty(value, "index", out var index) && index is >= 0 and <= 200));
    }

    private static bool HasValidLabScheduleRule(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("name", out var name)
            || name.ValueKind != JsonValueKind.String
            || name.GetString() is not { Length: > 0 and <= 32 }
            || !HasOnlyProperties(value, ["name", "enable", "wday", "repeat", "sact", "stime_opt", "smin", "soffset", "eact", "etime_opt", "emin"])
            || !HasOnlyIntegerPropertiesExcept(value, ["name", "wday"], ["enable", "repeat", "sact", "stime_opt", "smin", "soffset", "eact", "etime_opt", "emin"])
            || !TryGetIntProperty(value, "enable", out var enable)
            || !TryGetIntProperty(value, "repeat", out var repeat)
            || !TryGetIntProperty(value, "sact", out var sact)
            || !TryGetIntProperty(value, "stime_opt", out var stimeOpt)
            || !TryGetIntProperty(value, "smin", out var startMinute)
            || !TryGetIntProperty(value, "eact", out var eact)
            || !TryGetIntProperty(value, "soffset", out var offset)
            || !TryGetIntProperty(value, "etime_opt", out var etimeOpt)
            || !TryGetIntProperty(value, "emin", out var endMinute))
        {
            return false;
        }

        return enable is 1
            && repeat is 0 or 1
            && sact is 0 or 1
            && stimeOpt is 0
            && startMinute is >= 0 and <= 1439
            && eact is -1
            && offset is 0
            && etimeOpt is -1
            && endMinute is 0
            && HasValidWday(value);
    }

    private static bool HasValidLabCountdownRule(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("name", out var name)
            || name.ValueKind != JsonValueKind.String
            || name.GetString() is not ("HVO Lab" or "Timer AddTimerObject")
            || !HasOnlyIntegerPropertiesExcept(value, ["name"], ["enable", "delay", "act"])
            || !TryGetIntProperty(value, "enable", out var enable)
            || !TryGetIntProperty(value, "delay", out var delay)
            || !TryGetIntProperty(value, "act", out var act))
        {
            return false;
        }

        return enable is 1
            && delay is >= 5 and <= 300
            && act is 0 or 1;
    }

    private static bool HasValidLabCountdownEditRule(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || id.GetString() is not { } idText
            || !IsAllowedLabRuleId(idText)
            || !value.TryGetProperty("name", out var name)
            || name.ValueKind != JsonValueKind.String
            || name.GetString() is not "Timer AddTimerObject"
            || !HasOnlyIntegerPropertiesExcept(value, ["id", "name"], ["enable", "delay", "act"])
            || !TryGetIntProperty(value, "enable", out var enable)
            || !TryGetIntProperty(value, "delay", out var delay)
            || !TryGetIntProperty(value, "act", out var act))
        {
            return false;
        }

        return enable is 1
            && delay is >= 5 and <= 300
            && act is 0 or 1;
    }

    private static bool IsAllowedLabRuleId(string id) =>
        id.StartsWith(LabRuleIdPrefix, StringComparison.Ordinal)
        || (id.Length == 32 && id.All(Uri.IsHexDigit));

    private static bool HasValidWday(JsonElement value)
    {
        if (!value.TryGetProperty("wday", out var wday) || wday.ValueKind != JsonValueKind.Array || wday.GetArrayLength() != 7)
        {
            return false;
        }

        foreach (var day in wday.EnumerateArray())
        {
            if (day.ValueKind != JsonValueKind.Number || !day.TryGetInt32(out var dayValue) || dayValue is not 0 and not 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOnlyIntegerProperties(JsonElement value, string[] expectedProperties)
    {
        if (!HasOnlyProperties(value, expectedProperties))
        {
            return false;
        }

        return expectedProperties.All(name => TryGetIntProperty(value, name, out _));
    }

    private static bool HasOnlyIntegerPropertiesExcept(JsonElement value, string[] nonIntegerProperties, string[] integerProperties)
    {
        if (!HasOnlyProperties(value, [.. nonIntegerProperties, .. integerProperties]))
        {
            return false;
        }

        return integerProperties.All(name => TryGetIntProperty(value, name, out _));
    }

    private static bool HasOnlyProperties(JsonElement value, string[] expectedProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var actual = value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        var expected = expectedProperties.Order(StringComparer.Ordinal).ToArray();
        return actual.SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static bool TryGetIntProperty(JsonElement value, string name, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out result);
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Kasa TCP stream ended before the complete frame was read.");
            }

            offset += read;
        }
    }
}
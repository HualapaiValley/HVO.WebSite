using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaReadMetadataParser
{
    public KasaRuleMetadata ParseRules(JsonDocument response, params string[] path)
    {
        if (!TryGetNested(response.RootElement, path, out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaRuleMetadata(false, null, "Read metadata payload was not present.", null, null, null);
        }

        var error = GetError(payload);
        return new KasaRuleMetadata(
            error.IsSupported,
            error.ErrorCode,
            error.ErrorMessage,
            GetBool(payload, "enable"),
            GetInt(payload, "version"),
            payload.TryGetProperty("rule_list", out var rules) && rules.ValueKind == JsonValueKind.Array ? rules.GetArrayLength() : null);
    }

    public KasaNextActionMetadata ParseNextAction(JsonDocument response, params string[] path)
    {
        if (!TryGetNested(response.RootElement, path, out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaNextActionMetadata(false, null, "Read metadata payload was not present.", null);
        }

        var error = GetError(payload);
        return new KasaNextActionMetadata(error.IsSupported, error.ErrorCode, error.ErrorMessage, GetInt(payload, "type"));
    }

    public KasaDeviceTimeMetadata ParseTime(JsonDocument response, params string[] path)
    {
        if (!TryGetNested(response.RootElement, path, out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaDeviceTimeMetadata(false, null, "Read metadata payload was not present.", null, null, null, null, null, null);
        }

        var error = GetError(payload);
        return new KasaDeviceTimeMetadata(
            error.IsSupported,
            error.ErrorCode,
            error.ErrorMessage,
            GetInt(payload, "year"),
            GetInt(payload, "month"),
            GetInt(payload, "mday"),
            GetInt(payload, "hour"),
            GetInt(payload, "min"),
            GetInt(payload, "sec"));
    }

    public KasaTimezoneMetadata ParseTimezone(JsonDocument response, params string[] path)
    {
        if (!TryGetNested(response.RootElement, path, out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaTimezoneMetadata(false, null, "Read metadata payload was not present.", null);
        }

        var error = GetError(payload);
        return new KasaTimezoneMetadata(error.IsSupported, error.ErrorCode, error.ErrorMessage, GetInt(payload, "index"));
    }

    public KasaFirmwareDownloadMetadata ParseFirmwareDownload(JsonDocument response)
    {
        if (!TryGetNested(response.RootElement, ["system", "get_download_state"], out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaFirmwareDownloadMetadata(false, null, "Read metadata payload was not present.", null, null, null, null);
        }

        var error = GetError(payload);
        return new KasaFirmwareDownloadMetadata(
            error.IsSupported,
            error.ErrorCode,
            error.ErrorMessage,
            GetInt(payload, "status"),
            GetInt(payload, "ratio"),
            GetInt(payload, "flash_time"),
            GetInt(payload, "reboot_time"));
    }

    public KasaCloudMetadata ParseCloud(JsonDocument response)
    {
        if (!TryGetNested(response.RootElement, ["cnCloud", "get_info"], out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaCloudMetadata(false, null, "Read metadata payload was not present.", null, null, null, null, null, null);
        }

        var error = GetError(payload);
        return new KasaCloudMetadata(
            error.IsSupported,
            error.ErrorCode,
            error.ErrorMessage,
            GetBool(payload, "binded"),
            GetBool(payload, "cld_connection"),
            GetInt(payload, "fwNotifyType"),
            GetInt(payload, "illegalType"),
            GetBool(payload, "stopConnect"),
            GetInt(payload, "tcspStatus"));
    }

    public KasaFirmwareListMetadata ParseFirmwareList(JsonDocument response)
    {
        if (!TryGetNested(response.RootElement, ["cnCloud", "get_intl_fw_list"], out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaFirmwareListMetadata(false, null, "Read metadata payload was not present.", null);
        }

        var error = GetError(payload);
        return new KasaFirmwareListMetadata(
            error.IsSupported,
            error.ErrorCode,
            error.ErrorMessage,
            payload.TryGetProperty("fw_list", out var firmwareList) && firmwareList.ValueKind == JsonValueKind.Array ? firmwareList.GetArrayLength() : null);
    }

    public KasaDimmerDefaultBehaviorMetadata ParseDimmerDefaultBehavior(JsonDocument response)
    {
        if (!TryGetNested(response.RootElement, ["smartlife.iot.dimmer", "get_default_behavior"], out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaDimmerDefaultBehaviorMetadata(false, null, "Read metadata payload was not present.", null, null, null, null);
        }

        var error = GetError(payload);
        return new KasaDimmerDefaultBehaviorMetadata(
            error.IsSupported,
            error.ErrorCode,
            error.ErrorMessage,
            GetNestedString(payload, "soft_on", "mode"),
            GetNestedString(payload, "hard_on", "mode"),
            GetNestedString(payload, "double_click", "mode"),
            GetNestedString(payload, "long_press", "mode"));
    }

    public KasaDimmerParameterMetadata ParseDimmerParameters(JsonDocument response)
    {
        if (!TryGetNested(response.RootElement, ["smartlife.iot.dimmer", "get_dimmer_parameters"], out var payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return new KasaDimmerParameterMetadata(false, null, "Read metadata payload was not present.", null, null, null, null, null, null, null);
        }

        var error = GetError(payload);
        return new KasaDimmerParameterMetadata(
            error.IsSupported,
            error.ErrorCode,
            error.ErrorMessage,
            GetInt(payload, "bulb_type"),
            GetInt(payload, "fadeOnTime"),
            GetInt(payload, "fadeOffTime"),
            GetInt(payload, "gentleOnTime"),
            GetInt(payload, "gentleOffTime"),
            GetInt(payload, "minThreshold"),
            GetInt(payload, "rampRate"));
    }

    private static KasaReadError GetError(JsonElement payload)
    {
        var errorCode = GetInt(payload, "err_code");
        return new KasaReadError(errorCode.GetValueOrDefault(0) == 0, errorCode, GetString(payload, "err_msg"));
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

    private static bool? GetBool(JsonElement element, string name) =>
        GetInt(element, name) is int value ? value != 0 : null;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string propertyName, string nestedPropertyName) =>
        element.TryGetProperty(propertyName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, nestedPropertyName)
            : null;

    private sealed record KasaReadError(bool IsSupported, int? ErrorCode, string? ErrorMessage);
}

using System.Text;

namespace HVO.Edge.HomeAssistant.Mqtt;

public static class HomeAssistantMqttIdentity
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var result = new StringBuilder(value.Length * 2);
        foreach (var valueByte in Encoding.UTF8.GetBytes(value))
        {
            if (valueByte is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9')
                result.Append((char)valueByte);
            else
                result.Append("_x").Append(valueByte.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    public static string DeviceId(HomeAssistantDeviceKey key) =>
        $"hvo_{Segment(key.SiteId)}_{Segment(key.GatewayId)}_{Segment(key.DeviceId)}";

    public static string EntityUniqueId(HomeAssistantDeviceKey key, string componentId) =>
        $"{DeviceId(key)}_{Segment(componentId)}";

    public static string ReadableEntityId(HomeAssistantDeviceKey key, string componentId) =>
        $"hvo_{ReadableSegment(key.GatewayId)}_{ReadableSegment(key.DeviceId)}_{ReadableSegment(componentId)}";

    public static string GatewayClientId(string siteId, string gatewayId) =>
        $"hvo_{Segment(siteId)}_{Segment(gatewayId)}";

    private static string Segment(string value)
    {
        var normalized = Normalize(value);
        return $"{normalized.Length}x{normalized}";
    }

    private static string ReadableSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var result = new StringBuilder(value.Length);
        var separatorPending = false;
        foreach (var character in value.Trim())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separatorPending && result.Length > 0)
                    result.Append('_');
                result.Append(character);
                separatorPending = false;
            }
            else if (character is >= 'A' and <= 'Z')
            {
                if (separatorPending && result.Length > 0)
                    result.Append('_');
                result.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }
        }

        if (result.Length == 0)
            throw new ArgumentException("Home Assistant identity segments must contain an ASCII letter or digit.", nameof(value));
        return result.ToString();
    }
}

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

    public static string GatewayClientId(string siteId, string gatewayId) =>
        $"hvo_{Segment(siteId)}_{Segment(gatewayId)}";

    private static string Segment(string value)
    {
        var normalized = Normalize(value);
        return $"{normalized.Length}x{normalized}";
    }
}

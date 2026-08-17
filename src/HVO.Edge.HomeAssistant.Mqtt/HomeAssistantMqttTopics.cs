namespace HVO.Edge.HomeAssistant.Mqtt;

internal sealed record HomeAssistantMqttTopics(string DiscoveryPrefix, string TopicPrefix)
{
    public string GatewayAvailability(HomeAssistantDeviceKey key) =>
        $"{TrimmedTopicPrefix}/{HomeAssistantMqttIdentity.Normalize(key.SiteId)}/{HomeAssistantMqttIdentity.Normalize(key.GatewayId)}/availability";

    public string DeviceBase(HomeAssistantDeviceKey key) =>
        $"{TrimmedTopicPrefix}/{HomeAssistantMqttIdentity.Normalize(key.SiteId)}/{HomeAssistantMqttIdentity.Normalize(key.GatewayId)}/{HomeAssistantMqttIdentity.Normalize(key.DeviceId)}";

    public string DeviceAvailability(HomeAssistantDeviceKey key) => $"{DeviceBase(key)}/availability";
    public string State(HomeAssistantDeviceKey key) => $"{DeviceBase(key)}/state";
    public string Command(HomeAssistantDeviceKey key, string componentId) =>
        $"{DeviceBase(key)}/command/{HomeAssistantMqttIdentity.Normalize(componentId)}";
    public string CommandFilter(string siteId, string gatewayId) =>
        $"{TrimmedTopicPrefix}/{HomeAssistantMqttIdentity.Normalize(siteId)}/{HomeAssistantMqttIdentity.Normalize(gatewayId)}/+/command/+";
    public string Discovery(HomeAssistantDeviceKey key) => $"{TrimmedDiscoveryPrefix}/device/{HomeAssistantMqttIdentity.DeviceId(key)}/config";
    public string Birth => $"{TrimmedDiscoveryPrefix}/status";

    private string TrimmedDiscoveryPrefix => DiscoveryPrefix.Trim().Trim('/');
    private string TrimmedTopicPrefix => TopicPrefix.Trim().Trim('/');
}

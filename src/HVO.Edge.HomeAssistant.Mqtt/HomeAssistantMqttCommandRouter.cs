using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt;

internal sealed class HomeAssistantMqttCommandRouter(IOptions<HomeAssistantMqttOptions> options)
    : IHomeAssistantMqttCommandRouter
{
    private readonly HomeAssistantMqttTopics topics = new(options.Value.DiscoveryPrefix, options.Value.TopicPrefix);
    private readonly Lock sync = new();
    private readonly Dictionary<string, Action> handlers = new(StringComparer.Ordinal);

    public void Register(HomeAssistantDeviceKey key, string componentId, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var topic = topics.Command(key, componentId);
        lock (sync)
        {
            if (!handlers.TryAdd(topic, handler))
                throw new InvalidOperationException($"A Home Assistant command handler is already registered for '{topic}'.");
        }
    }

    public bool TryDispatch(string topic, string payload, bool retain)
    {
        if (retain || !string.Equals(payload.Trim(), "PRESS", StringComparison.Ordinal))
            return false;

        Action? handler;
        lock (sync)
            handlers.TryGetValue(topic, out handler);
        if (handler is null)
            return false;

        handler();
        return true;
    }
}

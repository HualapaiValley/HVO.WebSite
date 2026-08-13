using System.Collections.Immutable;
using System.Text.Json;

namespace HVO.Edge.HomeAssistant.Mqtt;

public sealed record HomeAssistantDeviceKey(string SiteId, string GatewayId, string DeviceId);

public enum HomeAssistantEntityPlatform
{
    Sensor,
    BinarySensor
}

public abstract record HomeAssistantEntityDefinition
{
    protected HomeAssistantEntityDefinition(
        string componentId,
        string name,
        HomeAssistantEntityPlatform platform,
        string? deviceClass,
        string? icon,
        string? entityCategory,
        bool enabledByDefault,
        string? defaultEntityId)
    {
        ComponentId = componentId;
        Name = name;
        Platform = platform;
        DeviceClass = deviceClass;
        Icon = icon;
        EntityCategory = entityCategory;
        EnabledByDefault = enabledByDefault;
        DefaultEntityId = defaultEntityId;
    }

    public string ComponentId { get; }
    public string Name { get; }
    public HomeAssistantEntityPlatform Platform { get; }
    public string? DeviceClass { get; }
    public string? Icon { get; }
    public string? EntityCategory { get; }
    public bool EnabledByDefault { get; }
    public string? DefaultEntityId { get; }
}

public sealed record HomeAssistantSensorDefinition : HomeAssistantEntityDefinition
{
    public HomeAssistantSensorDefinition(
        string componentId,
        string name,
        string? unitOfMeasurement = null,
        string? deviceClass = null,
        string? stateClass = null,
        string? icon = null,
        string? entityCategory = null,
        bool enabledByDefault = true,
        string? defaultEntityId = null,
        int? suggestedDisplayPrecision = null)
        : base(componentId, name, HomeAssistantEntityPlatform.Sensor, deviceClass, icon, entityCategory, enabledByDefault, defaultEntityId)
    {
        UnitOfMeasurement = unitOfMeasurement;
        StateClass = stateClass;
        SuggestedDisplayPrecision = suggestedDisplayPrecision;
    }

    public string? UnitOfMeasurement { get; }
    public string? StateClass { get; }
    public int? SuggestedDisplayPrecision { get; }
}

public sealed record HomeAssistantBinarySensorDefinition : HomeAssistantEntityDefinition
{
    public HomeAssistantBinarySensorDefinition(
        string componentId,
        string name,
        string? deviceClass = null,
        string? icon = null,
        string? entityCategory = null,
        bool enabledByDefault = true,
        string? defaultEntityId = null)
        : base(componentId, name, HomeAssistantEntityPlatform.BinarySensor, deviceClass, icon, entityCategory, enabledByDefault, defaultEntityId)
    {
    }
}

public sealed record HomeAssistantDeviceDefinition
{
    public HomeAssistantDeviceDefinition(
        HomeAssistantDeviceKey key,
        string name,
        IEnumerable<HomeAssistantEntityDefinition> entities,
        string? manufacturer = null,
        string? model = null,
        string? softwareVersion = null,
        string? hardwareVersion = null)
    {
        Key = key;
        Name = name;
        Entities = entities.ToImmutableArray();
        Manufacturer = manufacturer;
        Model = model;
        SoftwareVersion = softwareVersion;
        HardwareVersion = hardwareVersion;
    }

    public HomeAssistantDeviceKey Key { get; }
    public string Name { get; }
    public ImmutableArray<HomeAssistantEntityDefinition> Entities { get; }
    public string? Manufacturer { get; }
    public string? Model { get; }
    public string? SoftwareVersion { get; }
    public string? HardwareVersion { get; }
}

public sealed record HomeAssistantCurrentState
{
    public HomeAssistantCurrentState(
        HomeAssistantDeviceKey deviceKey,
        DateTimeOffset observedAtUtc,
        IEnumerable<KeyValuePair<string, JsonElement>> componentValues,
        bool available = true)
    {
        DeviceKey = deviceKey;
        ObservedAtUtc = observedAtUtc;
        ComponentValues = componentValues.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Available = available;
    }

    public HomeAssistantDeviceKey DeviceKey { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public ImmutableDictionary<string, JsonElement> ComponentValues { get; }
    public bool Available { get; }
}

public sealed record HomeAssistantMqttStatus(
    bool Enabled,
    bool Connected,
    int DeviceCount,
    DateTimeOffset? LastConnectedAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastFailureCategory);

public interface IHomeAssistantMqttProjection
{
    void UpsertDevice(HomeAssistantDeviceDefinition definition);
    bool PublishCurrentState(HomeAssistantCurrentState state);
    bool RemoveDevice(HomeAssistantDeviceKey key);
    HomeAssistantMqttStatus GetStatus();
}

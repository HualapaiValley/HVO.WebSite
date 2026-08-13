using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;
using HVO.Edge.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt;

internal sealed class HomeAssistantMqttProjection : IHomeAssistantMqttProjection
{
    private readonly object sync = new();
    private readonly Dictionary<HomeAssistantDeviceKey, DeviceEntry> devices = [];
    private readonly Dictionary<HomeAssistantDeviceKey, HomeAssistantDeviceDefinition> removals = [];
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly EdgeRuntimeIdentity identity;
    private HomeAssistantMqttStatus status;

    public HomeAssistantMqttProjection(EdgeRuntimeIdentity identity, IOptions<HomeAssistantMqttOptions> options)
    {
        this.identity = identity;
        status = new(options.Value.Enabled, false, 0, null, null, null);
    }

    public void UpsertDevice(HomeAssistantDeviceDefinition definition)
    {
        HomeAssistantDiscoverySerializer.Validate(definition);
        ValidateRuntimeIdentity(definition.Key);

        lock (sync)
        {
            var normalizedDeviceId = HomeAssistantMqttIdentity.DeviceId(definition.Key);
            if (devices.Keys.Concat(removals.Keys).Any(key =>
                    key != definition.Key
                    && string.Equals(HomeAssistantMqttIdentity.DeviceId(key), normalizedDeviceId, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"Device identifier normalization collision for '{normalizedDeviceId}'.",
                    nameof(definition));
            }

            devices.TryGetValue(definition.Key, out var existing);
            var currentComponents = definition.Entities
                .Select(entity => HomeAssistantMqttIdentity.Normalize(entity.ComponentId))
                .ToHashSet(StringComparer.Ordinal);
            var removedComponents = (existing?.RemovedComponents ?? [])
                .Concat(existing?.Definition.Entities ?? [])
                .Where(entity => !currentComponents.Contains(HomeAssistantMqttIdentity.Normalize(entity.ComponentId)))
                .DistinctBy(entity => HomeAssistantMqttIdentity.Normalize(entity.ComponentId), StringComparer.Ordinal)
                .ToImmutableArray();
            devices[definition.Key] = new(definition, existing?.State, removedComponents);
            removals.Remove(definition.Key);
            status = status with { DeviceCount = devices.Count };
        }
        Signal();
    }

    public bool PublishCurrentState(HomeAssistantCurrentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateRuntimeIdentity(state.DeviceKey);

        lock (sync)
        {
            if (!devices.TryGetValue(state.DeviceKey, out var entry))
                throw new InvalidOperationException("The Home Assistant device must be registered before publishing state.");
            if (entry.State is not null
                && state.ObservedAtUtc <= entry.State.ObservedAtUtc
                && (entry.State.Available || entry.State.ComponentValues.Count > 0 || !state.Available))
                return false;

            var componentMap = entry.Definition.Entities.ToDictionary(
                entity => entity.ComponentId,
                entity => HomeAssistantMqttIdentity.Normalize(entity.ComponentId),
                StringComparer.Ordinal);
            var normalizedValues = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
            foreach (var pair in state.ComponentValues)
            {
                if (!componentMap.TryGetValue(pair.Key, out var normalizedId))
                    throw new ArgumentException($"State contains unknown component '{pair.Key}'.", nameof(state));
                normalizedValues.Add(normalizedId, pair.Value.Clone());
            }

            var normalizedState = new HomeAssistantCurrentState(
                state.DeviceKey,
                state.ObservedAtUtc.ToUniversalTime(),
                normalizedValues,
                state.Available);
            devices[state.DeviceKey] = entry with { State = normalizedState };
        }
        Signal();
        return true;
    }

    public bool RemoveDevice(HomeAssistantDeviceKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateRuntimeIdentity(key);
        lock (sync)
        {
            if (!devices.Remove(key, out var entry))
                return false;
            removals[key] = entry.Definition;
            status = status with { DeviceCount = devices.Count };
        }
        Signal();
        return true;
    }

    public HomeAssistantMqttStatus GetStatus()
    {
        lock (sync)
            return status;
    }

    internal ProjectionSnapshot Snapshot()
    {
        lock (sync)
            return new(devices.Values.ToImmutableArray(), removals.Values.ToImmutableArray());
    }

    internal void CompleteRemovals(IEnumerable<HomeAssistantDeviceDefinition> definitions)
    {
        lock (sync)
        {
            foreach (var definition in definitions)
            {
                if (removals.TryGetValue(definition.Key, out var current)
                    && ReferenceEquals(current, definition))
                {
                    removals.Remove(definition.Key);
                }
            }
        }
    }

    internal void CompleteComponentRemovals(HomeAssistantDeviceKey key, ImmutableArray<HomeAssistantEntityDefinition> removed)
    {
        if (removed.IsDefaultOrEmpty)
            return;
        lock (sync)
        {
            if (devices.TryGetValue(key, out var entry)
                && entry.RemovedComponents.SequenceEqual(removed))
            {
                devices[key] = entry with { RemovedComponents = [] };
            }
        }
    }

    internal async ValueTask WaitForChangeAsync(CancellationToken cancellationToken)
    {
        await changes.Reader.ReadAsync(cancellationToken);
        while (changes.Reader.TryRead(out _))
        {
        }
    }

    internal void DiscardChanges()
    {
        while (changes.Reader.TryRead(out _))
        {
        }
    }

    internal void Signal() => changes.Writer.TryWrite(true);

    internal void SetConnected(bool connected)
    {
        lock (sync)
            status = status with
            {
                Connected = connected,
                LastConnectedAtUtc = connected ? DateTimeOffset.UtcNow : status.LastConnectedAtUtc,
                LastFailureCategory = connected ? null : status.LastFailureCategory
            };
    }

    internal void SetFailure(string category)
    {
        lock (sync)
            status = status with
            {
                Connected = false,
                LastFailureAtUtc = DateTimeOffset.UtcNow,
                LastFailureCategory = category
            };
    }

    private void ValidateRuntimeIdentity(HomeAssistantDeviceKey key)
    {
        if (string.IsNullOrWhiteSpace(identity.SiteId))
            throw new InvalidOperationException("Edge:Runtime:SiteId is required for Home Assistant MQTT projection.");
        if (!string.Equals(key.SiteId, identity.SiteId, StringComparison.Ordinal)
            || !string.Equals(key.GatewayId, identity.GatewayId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Device site and gateway identifiers must match the configured edge runtime identity.", nameof(key));
        }
    }

    internal sealed record DeviceEntry(
        HomeAssistantDeviceDefinition Definition,
        HomeAssistantCurrentState? State,
        ImmutableArray<HomeAssistantEntityDefinition> RemovedComponents);

    internal sealed record ProjectionSnapshot(
        ImmutableArray<DeviceEntry> Devices,
        ImmutableArray<HomeAssistantDeviceDefinition> Removals);
}

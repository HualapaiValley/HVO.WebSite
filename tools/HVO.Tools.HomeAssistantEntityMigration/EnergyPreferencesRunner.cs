using System.Text.Json;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal sealed class EnergyPreferencesRunner(
    IHomeAssistantRegistryClient client,
    EnergyPreferencesManifest manifest)
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        EnsureVersion();
        await ValidateEntitiesAsync(cancellationToken);
        var current = await client.GetEnergyPreferencesAsync(cancellationToken);
        var desired = BuildDesired(current);
        if (current is null || !Equivalent(current.Value, desired))
            throw new InvalidOperationException("Home Assistant Energy preferences do not match the managed HVO manifest.");
        await EnsureEnergyValidAsync(cancellationToken);
        Console.WriteLine("Home Assistant Energy preferences match the managed off-grid manifest.");
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        EnsureVersion();
        await ValidateEntitiesAsync(cancellationToken);
        var previous = await client.GetEnergyPreferencesAsync(cancellationToken);
        var desired = BuildDesired(previous);
        try
        {
            await SaveAsync(desired, cancellationToken);
            var actual = await client.GetEnergyPreferencesAsync(cancellationToken)
                ?? throw new InvalidOperationException("Home Assistant did not persist Energy preferences.");
            if (!Equivalent(actual, desired))
                throw new InvalidOperationException("Persisted Home Assistant Energy preferences do not match the requested configuration.");
            await EnsureEnergyValidAsync(cancellationToken);
        }
        catch
        {
            await SaveAsync(previous ?? EmptyPreferences(), cancellationToken);
            throw;
        }
        Console.WriteLine("Applied and validated managed HVO off-grid Energy preferences.");
    }

    private Task SaveAsync(JsonElement preferences, CancellationToken cancellationToken) =>
        client.SaveEnergyPreferencesAsync(
            preferences.GetProperty("energy_sources").EnumerateArray().Select(static item => item.Clone()).ToArray(),
            preferences.GetProperty("device_consumption").EnumerateArray().Select(static item => item.Clone()).ToArray(),
            preferences.TryGetProperty("device_consumption_water", out var water)
                ? water.EnumerateArray().Select(static item => item.Clone()).ToArray()
                : [],
            cancellationToken);

    private static JsonElement EmptyPreferences() => JsonSerializer.SerializeToElement(new
    {
        energy_sources = Array.Empty<object>(),
        device_consumption = Array.Empty<object>(),
        device_consumption_water = Array.Empty<object>(),
    });

    private JsonElement BuildDesired(JsonElement? current)
    {
        var sources = PreserveUnmanaged(current, "energy_sources").Concat(manifest.EnergySources).ToArray();
        var devices = PreserveUnmanaged(current, "device_consumption").Concat(manifest.DeviceConsumption).ToArray();
        var water = current is { } preferences && preferences.TryGetProperty("device_consumption_water", out var currentWater)
            ? currentWater.EnumerateArray().Select(static item => item.Clone()).ToArray()
            : [];
        return JsonSerializer.SerializeToElement(new
        {
            energy_sources = sources,
            device_consumption = devices,
            device_consumption_water = water,
        });
    }

    private IEnumerable<JsonElement> PreserveUnmanaged(JsonElement? preferences, string propertyName)
    {
        if (preferences is not { } value || !value.TryGetProperty(propertyName, out var entries))
            return [];
        return entries.EnumerateArray()
            .Where(entry => !entry.TryGetProperty("name", out var name) || !manifest.ManagedNames.Contains(name.GetString() ?? string.Empty))
            .Select(static entry => entry.Clone())
            .ToArray();
    }

    private async Task ValidateEntitiesAsync(CancellationToken cancellationToken)
    {
        var states = (await client.ListStatesAsync(cancellationToken))
            .ToDictionary(state => state.GetProperty("entity_id").GetString()!, StringComparer.Ordinal);
        var missing = manifest.ReferencedEntities.Where(entityId => !states.ContainsKey(entityId)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Energy manifest entities are missing: {string.Join(", ", missing)}");
    }

    private async Task EnsureEnergyValidAsync(CancellationToken cancellationToken)
    {
        var validation = await client.ValidateEnergyAsync(cancellationToken);
        if (validation.TryGetProperty("energy_sources", out var sources)
            && sources.ValueKind == JsonValueKind.Object
            && sources.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.Object
                && property.Value.EnumerateObject().Any()))
            throw new InvalidOperationException($"Home Assistant Energy validation failed: {validation}");
    }

    private void EnsureVersion()
    {
        if (!string.Equals(client.Version, manifest.HomeAssistantVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Home Assistant {client.Version} does not match pinned Energy version {manifest.HomeAssistantVersion}.");
    }

    private static bool Equivalent(JsonElement actual, JsonElement expected) =>
        JsonNode(actual) == JsonNode(expected);

    private static string JsonNode(JsonElement value) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = false,
    });
}

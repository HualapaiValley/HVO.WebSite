using System.Text.Json;
using System.Globalization;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal sealed class EnergyPreferencesRunner(
    IHomeAssistantRegistryClient client,
    EnergyPreferencesManifest manifest)
{
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(60);

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        EnsureVersion();
        await ValidateEntitiesAsync(cancellationToken);
        var current = await client.GetEnergyPreferencesAsync(cancellationToken);
        var desired = BuildDesired(current);
        if (current is null || !Equivalent(current.Value, desired))
            throw new InvalidOperationException(
                $"Home Assistant Energy preferences do not match the managed HVO manifest. " +
                $"Current managed entries: {DescribeManaged(current)}. Desired managed entries: {DescribeManaged(desired)}.");
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
        catch (Exception original)
        {
            using var rollbackCancellation = new CancellationTokenSource(RollbackTimeout);
            await RollbackAsync(previous ?? EmptyPreferences(), original, rollbackCancellation.Token);
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
        var devices = PreserveUnmanagedDeviceConsumption(current).Concat(manifest.DeviceConsumption).ToArray();
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

    private IEnumerable<JsonElement> PreserveUnmanagedDeviceConsumption(JsonElement? preferences)
    {
        var removedEntities = manifest.RemovedDeviceConsumptionEntities.ToHashSet(StringComparer.Ordinal);
        return PreserveUnmanaged(preferences, "device_consumption")
            .Where(entry => !entry.TryGetProperty("stat_consumption", out var entity)
                || !removedEntities.Contains(entity.GetString() ?? string.Empty));
    }

    private async Task ValidateEntitiesAsync(CancellationToken cancellationToken)
    {
        var states = (await client.ListStatesAsync(cancellationToken))
            .ToDictionary(state => state.GetProperty("entity_id").GetString()!, StringComparer.Ordinal);
        var missing = manifest.ReferencedEntities.Where(entityId => !states.ContainsKey(entityId)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Energy manifest entities are missing: {string.Join(", ", missing)}");

        var statistics = (await client.ListStatisticIdsAsync(cancellationToken))
            .ToDictionary(item => item.GetProperty("statistic_id").GetString()!, StringComparer.Ordinal);
        foreach (var property in manifest.EnergySources.Concat(manifest.DeviceConsumption)
                     .SelectMany(static entry => entry.EnumerateObject())
                     .Where(static property => property.Name.StartsWith("stat_", StringComparison.Ordinal)
                         && property.Name != "included_in_stat"))
        {
            var entityId = property.Value.GetString()!;
            var state = states[entityId];
            var attributes = state.GetProperty("attributes");
            var statistic = statistics.TryGetValue(entityId, out var value)
                ? value
                : throw new InvalidOperationException($"Energy entity has no recorder statistics metadata: {entityId}.");
            switch (property.Name)
            {
                case "stat_rate":
                    ValidateOptionalNumericCurrentState(state, entityId, "rate");
                    EnsureAttribute(attributes, entityId, "device_class", "power");
                    EnsureAttribute(attributes, entityId, "state_class", "measurement");
                    EnsureStatisticEligibility(statistic, entityId, "has_mean", "power");
                    break;
                case "stat_soc":
                    var soc = ValidateOptionalNumericCurrentState(state, entityId, "SOC");
                    if (soc is < 0 or > 100)
                        throw new InvalidOperationException($"Energy SOC entity is outside 0-100: {entityId}.");
                    EnsureAttribute(attributes, entityId, "device_class", "battery");
                    EnsureAttribute(attributes, entityId, "state_class", "measurement");
                    EnsureAttribute(attributes, entityId, "unit_of_measurement", "%");
                    EnsureStatisticEligibility(statistic, entityId, "has_mean", "unitless");
                    break;
                default:
                    EnsureAttribute(attributes, entityId, "device_class", "energy");
                    var stateClass = attributes.TryGetProperty("state_class", out var stateClassValue)
                        ? stateClassValue.GetString()
                        : null;
                    if (stateClass is not ("total" or "total_increasing"))
                        throw new InvalidOperationException($"Energy cumulative entity has invalid state_class: {entityId}.");
                    EnsureStatisticEligibility(statistic, entityId, "has_sum", "energy");
                    break;
            }
        }
    }

    private async Task RollbackAsync(JsonElement previous, Exception original, CancellationToken cancellationToken)
    {
        try
        {
            await SaveAsync(previous, cancellationToken);
            var restored = await client.GetEnergyPreferencesAsync(cancellationToken)
                ?? throw new InvalidOperationException("Home Assistant returned no Energy preferences after rollback.");
            if (!Equivalent(restored, previous))
                throw new InvalidOperationException("Home Assistant Energy preference rollback did not match the previous configuration.");
        }
        catch (Exception rollback)
        {
            throw new AggregateException("Energy preference apply failed and rollback could not be verified.", original, rollback);
        }
    }

    private static double? ValidateOptionalNumericCurrentState(JsonElement state, string entityId, string role)
    {
        var value = state.GetProperty("state").GetString();
        if (value is "unknown" or "unavailable")
            return null;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new InvalidOperationException($"Energy {role} entity has a malformed current state: {entityId}.");
        return number;
    }

    private static void EnsureAttribute(JsonElement attributes, string entityId, string name, string expected)
    {
        if (!attributes.TryGetProperty(name, out var value) || value.GetString() != expected)
            throw new InvalidOperationException($"Energy entity {entityId} requires {name}={expected}.");
    }

    private static void EnsureStatisticEligibility(JsonElement statistic, string entityId, string capability, string unitClass)
    {
        if (!statistic.TryGetProperty(capability, out var eligible) || !eligible.GetBoolean())
            throw new InvalidOperationException($"Energy entity is not eligible for recorder {capability} statistics: {entityId}.");
        if (!statistic.TryGetProperty("unit_class", out var actualUnitClass) || actualUnitClass.GetString() != unitClass)
            throw new InvalidOperationException($"Energy entity {entityId} requires recorder unit_class={unitClass}.");
    }

    private async Task EnsureEnergyValidAsync(CancellationToken cancellationToken)
    {
        var validation = await client.ValidateEnergyAsync(cancellationToken);
        EnsureEnergyValidationSucceeded(validation);
    }

    internal static void EnsureEnergyValidationSucceeded(JsonElement validation)
    {
        var errors = validation.ValueKind == JsonValueKind.Object
            ? validation.EnumerateObject().Where(property => HasValidationError(property.Value)).Select(property => property.Name).ToArray()
            : ["response"];
        if (errors.Length > 0)
            throw new InvalidOperationException(
                $"Home Assistant Energy validation failed in {string.Join(", ", errors)}: {validation}");
    }

    private static bool HasValidationError(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray().Any(HasValidationError),
        JsonValueKind.Object => value.EnumerateObject().Any(property => HasValidationError(property.Value)),
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        _ => true,
    };

    private string DescribeManaged(JsonElement? preferences)
    {
        if (preferences is not { } value)
            return "none";
        return string.Join(", ", new[] { "energy_sources", "device_consumption" }
            .SelectMany(propertyName => value.TryGetProperty(propertyName, out var entries)
                ? entries.EnumerateArray()
                    .Where(entry => entry.TryGetProperty("name", out var name) && manifest.ManagedNames.Contains(name.GetString() ?? string.Empty))
                    .Select(entry => entry.GetProperty("name").GetString())
                : []));
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

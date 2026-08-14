using System.Text.Json;
using System.Text.RegularExpressions;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal sealed record EnergyPreferencesManifest(
    string HomeAssistantVersion,
    JsonElement[] EnergySources,
    JsonElement[] DeviceConsumption,
    string[] RemovedDeviceConsumptionEntities,
    string[] ReplacedManagedNames)
{
    private static readonly Regex EntityIdPattern = new("^[a-z0-9_]+\\.[a-z0-9_]+$", RegexOptions.CultureInvariant);

    public static async Task<EnergyPreferencesManifest> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<EnergyPreferencesManifest>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken) ?? throw new InvalidOperationException("The Energy preferences manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    public IReadOnlySet<string> ManagedNames => EnergySources.Concat(DeviceConsumption)
        .Select(item => item.GetProperty("name").GetString()!)
        .Concat(ReplacedManagedNames)
        .ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> ReferencedEntities => EnergySources.Concat(DeviceConsumption)
        .SelectMany(static item => item.EnumerateObject())
        .Where(static property => property.Name.StartsWith("stat_", StringComparison.Ordinal))
        .SelectMany(static property => property.Value.ValueKind == JsonValueKind.Object
            ? property.Value.EnumerateObject().Select(static nested => nested.Value.GetString())
            : [property.Value.GetString()])
        .Where(static value => !string.IsNullOrWhiteSpace(value) && EntityIdPattern.IsMatch(value))
        .Cast<string>()
        .ToHashSet(StringComparer.Ordinal);

    private void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(HomeAssistantVersion);
        if (EnergySources.Length == 0)
            throw new InvalidOperationException("At least one Energy source is required.");
        if (EnergySources.Any(source => source.GetProperty("type").GetString() == "grid"))
            throw new InvalidOperationException("The HVO off-grid Energy manifest must not contain a grid source.");
        var names = EnergySources.Concat(DeviceConsumption)
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidOperationException("Every managed Energy entry requires a unique name.");
        if (ReferencedEntities.Count == 0)
            throw new InvalidOperationException("The Energy manifest does not reference any entities.");
        if (RemovedDeviceConsumptionEntities.Any(entityId => !EntityIdPattern.IsMatch(entityId)))
            throw new InvalidOperationException("Every removed Energy device-consumption entity must be a valid entity ID.");
        if (ReplacedManagedNames.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Every replaced managed Energy name must be non-empty.");
        ValidateDeviceConsumptionRelationships();
    }

    private void ValidateDeviceConsumptionRelationships()
    {
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in DeviceConsumption)
        {
            if (!entry.TryGetProperty("stat_consumption", out var consumption)
                || consumption.ValueKind != JsonValueKind.String
                || !EntityIdPattern.IsMatch(consumption.GetString() ?? string.Empty))
                throw new InvalidOperationException("Every device-consumption entry requires a valid stat_consumption entity ID.");
            var entityId = consumption.GetString()!;
            if (!parents.TryAdd(entityId, null))
                throw new InvalidOperationException($"Device-consumption stat_consumption is duplicated: {entityId}.");
        }

        foreach (var entry in DeviceConsumption)
        {
            if (!entry.TryGetProperty("included_in_stat", out var included))
                continue;
            if (included.ValueKind != JsonValueKind.String || !EntityIdPattern.IsMatch(included.GetString() ?? string.Empty))
                throw new InvalidOperationException("Every included_in_stat value must be a valid entity ID.");
            var entityId = entry.GetProperty("stat_consumption").GetString()!;
            var parentId = included.GetString()!;
            if (!parents.ContainsKey(parentId))
                throw new InvalidOperationException($"Device-consumption parent is not configured: {parentId}.");
            if (entityId == parentId)
                throw new InvalidOperationException($"Device-consumption entry cannot include itself: {entityId}.");
            parents[entityId] = parentId;
        }

        foreach (var entityId in parents.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (var current = entityId; parents[current] is { } parent; current = parent)
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException($"Device-consumption included_in_stat cycle contains {current}.");
            }
        }
    }
}

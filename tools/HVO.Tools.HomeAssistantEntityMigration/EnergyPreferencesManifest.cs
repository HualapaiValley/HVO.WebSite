using System.Text.Json;
using System.Text.RegularExpressions;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal sealed record EnergyPreferencesManifest(
    string HomeAssistantVersion,
    JsonElement[] EnergySources,
    JsonElement[] DeviceConsumption)
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
    }
}

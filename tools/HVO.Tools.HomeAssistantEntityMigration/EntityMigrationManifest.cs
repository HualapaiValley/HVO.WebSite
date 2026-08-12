using System.Text.Json;
using System.Text.RegularExpressions;
using HVO.Edge.HomeAssistant.Mqtt;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal sealed record EntityMigrationManifest(
    string MigrationId,
    string HomeAssistantVersion,
    string SiteId,
    string GatewayId,
    string DeviceId,
    EntityMigrationEntry[] Entities)
{
    private static readonly Regex EntityIdPattern = new("^sensor\\.[a-z0-9_]+$", RegexOptions.CultureInvariant);

    public static async Task<EntityMigrationManifest> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<EntityMigrationManifest>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken) ?? throw new InvalidOperationException("The migration manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    public IReadOnlyList<ResolvedEntityMigration> Resolve()
    {
        var key = new HomeAssistantDeviceKey(SiteId, GatewayId, DeviceId);
        return Entities.Select(entity => new ResolvedEntityMigration(
            entity.ComponentId,
            HomeAssistantMqttIdentity.EntityUniqueId(key, entity.ComponentId),
            $"sensor.{HomeAssistantMqttIdentity.EntityUniqueId(key, entity.ComponentId)}",
            entity.TargetEntityId,
            entity.EnabledByDefault)).ToArray();
    }

    private void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(MigrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(HomeAssistantVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(SiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(GatewayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeviceId);
        if (Entities is not { Length: 39 })
            throw new InvalidOperationException("The Davis migration manifest must contain exactly 39 entities.");
        if (Entities.Any(entity => string.IsNullOrWhiteSpace(entity.ComponentId)))
            throw new InvalidOperationException("Every migration entry requires a component ID.");
        if (Entities.Any(entity => !EntityIdPattern.IsMatch(entity.TargetEntityId)))
            throw new InvalidOperationException("Every target must be a lowercase Home Assistant sensor entity ID.");
        if (Entities.Select(entity => entity.ComponentId).Distinct(StringComparer.Ordinal).Count() != Entities.Length)
            throw new InvalidOperationException("The migration manifest contains a component ID collision.");
        if (Entities.Select(entity => entity.TargetEntityId).Distinct(StringComparer.Ordinal).Count() != Entities.Length)
            throw new InvalidOperationException("The migration manifest contains a target entity ID collision.");
    }
}

internal sealed record EntityMigrationEntry(string ComponentId, string TargetEntityId, bool EnabledByDefault);

internal sealed record ResolvedEntityMigration(
    string ComponentId,
    string UniqueId,
    string SourceEntityId,
    string TargetEntityId,
    bool EnabledByDefault);

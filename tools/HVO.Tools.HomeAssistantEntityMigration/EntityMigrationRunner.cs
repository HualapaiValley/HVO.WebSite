using System.Text.Json;

namespace HVO.Tools.HomeAssistantEntityMigration;

internal sealed class EntityMigrationRunner(
    IHomeAssistantRegistryClient client,
    EntityMigrationManifest manifest,
    string backupPath,
    string trackedConfigurationPath)
{
    private static readonly HashSet<string> NonReferenceRelationshipTypes =
        ["area", "config_entry", "device", "entity", "floor", "integration", "label"];

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(cancellationToken);
        PrintPlan(plan);
        EnsureNoBlockingReferences(plan);
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var plan = await BuildPlanAsync(cancellationToken);
        var homeAssistantBackupId = await client.CreateBackupAsync(
            $"Before {manifest.MigrationId}",
            cancellationToken);
        await WriteBackupAsync(plan, homeAssistantBackupId, cancellationToken);
        var pending = plan.Where(item => item.CurrentEntityId == item.Expected.SourceEntityId).ToArray();
        EnsureNoBlockingReferences(plan);
        foreach (var item in pending)
            await client.RenameAsync(item.Expected.SourceEntityId, item.Expected.TargetEntityId, cancellationToken);
        await VerifyAsync(plan, useTargets: true, cancellationToken);
        Console.WriteLine("Migrated {0} Davis entities; {1} were already migrated.", pending.Length, plan.Count - pending.Length);
    }

    public async Task RollbackAsync(string rollbackPath, CancellationToken cancellationToken)
    {
        if (!string.Equals(client.Version, manifest.HomeAssistantVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Home Assistant {client.Version} does not match pinned migration version {manifest.HomeAssistantVersion}.");

        await using var stream = File.OpenRead(rollbackPath);
        var backup = await JsonSerializer.DeserializeAsync<MigrationBackup>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The migration backup is empty.");
        var resolved = manifest.Resolve();
        var backupByUniqueId = ValidateBackup(manifest, backup, resolved);

        var entries = await client.ListEntitiesAsync(cancellationToken);
        var byEntityId = entries.ToDictionary(entry => entry.GetProperty("entity_id").GetString()!, StringComparer.Ordinal);
        var rollbackItems = new List<MigrationBackupEntry>();
        foreach (var item in backup.Entities)
        {
            var current = FindByUniqueId(entries, item.UniqueId);
            var currentEntityId = current.GetProperty("entity_id").GetString();
            if (currentEntityId != item.SourceEntityId && currentEntityId != item.TargetEntityId)
                throw new InvalidOperationException($"Entity {item.UniqueId} is not at the expected rollback target.");
            if (byEntityId.TryGetValue(item.SourceEntityId, out var source)
                && source.GetProperty("unique_id").GetString() != item.UniqueId)
                throw new InvalidOperationException($"Rollback source entity ID {item.SourceEntityId} is occupied.");
            if (currentEntityId == item.TargetEntityId)
                rollbackItems.Add(item);
        }
        foreach (var item in rollbackItems)
            await client.RenameAsync(item.TargetEntityId, item.SourceEntityId, cancellationToken);
        var expectedPlan = resolved.Select(item =>
        {
            var original = backupByUniqueId[item.UniqueId][0];
            return new MigrationPlanItem(
                item,
                item.TargetEntityId,
                original.RegistryId,
                original.DeviceId,
                original.DisabledBy,
                original.Related,
                []);
        }).ToArray();
        await VerifyAsync(expectedPlan, useTargets: false, cancellationToken);
        Console.WriteLine("Rolled back all Davis entity IDs from {0}.", rollbackPath);
    }

    internal static IReadOnlyDictionary<string, MigrationBackupEntry[]> ValidateBackup(
        EntityMigrationManifest manifest,
        MigrationBackup backup,
        IReadOnlyList<ResolvedEntityMigration>? resolvedManifest = null)
    {
        if (!string.Equals(backup.MigrationId, manifest.MigrationId, StringComparison.Ordinal))
            throw new InvalidOperationException("The backup does not belong to this migration.");
        if (!string.Equals(backup.HomeAssistantVersion, manifest.HomeAssistantVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The backup does not match the pinned Home Assistant version.");
        var resolved = resolvedManifest ?? manifest.Resolve();
        var backupByUniqueId = backup.Entities
            .GroupBy(item => item.UniqueId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (backup.Entities.Length != resolved.Count
            || backupByUniqueId.Count != resolved.Count
            || backupByUniqueId.Values.Any(items => items.Length != 1))
            throw new InvalidOperationException("The backup does not contain exactly one entry for every manifest entity.");
        foreach (var expected in resolved)
        {
            if (!backupByUniqueId.TryGetValue(expected.UniqueId, out var items)
                || items[0].ComponentId != expected.ComponentId
                || items[0].SourceEntityId != expected.SourceEntityId
                || items[0].TargetEntityId != expected.TargetEntityId)
                throw new InvalidOperationException($"The backup entry for {expected.UniqueId} does not match the migration manifest.");
        }
        return backupByUniqueId;
    }

    private async Task<IReadOnlyList<MigrationPlanItem>> BuildPlanAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(client.Version, manifest.HomeAssistantVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Home Assistant {client.Version} does not match pinned migration version {manifest.HomeAssistantVersion}.");

        var entries = await client.ListEntitiesAsync(cancellationToken);
        var byEntityId = entries.ToDictionary(entry => entry.GetProperty("entity_id").GetString()!, StringComparer.Ordinal);
        var dashboards = await client.GetLovelaceConfigurationsAsync(cancellationToken);
        var plan = new List<MigrationPlanItem>();
        foreach (var expected in manifest.Resolve())
        {
            var current = FindByUniqueId(entries, expected.UniqueId);
            var currentEntityId = current.GetProperty("entity_id").GetString()!;
            if (current.GetProperty("platform").GetString() != "mqtt")
                throw new InvalidOperationException($"Entity {currentEntityId} is not owned by the MQTT integration.");
            var disabledBy = current.GetProperty("disabled_by").GetString();
            var expectedDisabledBy = expected.EnabledByDefault ? null : "integration";
            if (disabledBy != expectedDisabledBy)
                throw new InvalidOperationException($"Entity {currentEntityId} has unexpected disabled state '{disabledBy ?? "enabled"}'.");
            if (currentEntityId != expected.SourceEntityId && currentEntityId != expected.TargetEntityId)
                throw new InvalidOperationException($"Entity {expected.UniqueId} has unexpected entity ID {currentEntityId}.");
            if (byEntityId.TryGetValue(expected.TargetEntityId, out var target)
                && target.GetProperty("unique_id").GetString() != expected.UniqueId)
                throw new InvalidOperationException($"Target entity ID {expected.TargetEntityId} is already occupied.");
            if (byEntityId.TryGetValue(expected.SourceEntityId, out var source)
                && source.GetProperty("unique_id").GetString() != expected.UniqueId)
                throw new InvalidOperationException($"Source entity ID {expected.SourceEntityId} is occupied by a duplicate entity.");

            var related = await client.FindRelatedAsync(expected.SourceEntityId, cancellationToken);
            var blockingReferences = related.EnumerateObject()
                .Where(property => !NonReferenceRelationshipTypes.Contains(property.Name)
                    && property.Value.ValueKind == JsonValueKind.Array
                    && property.Value.GetArrayLength() > 0)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (dashboards.Any(dashboard => dashboard.GetRawText().Contains(expected.SourceEntityId, StringComparison.Ordinal)))
                blockingReferences = [.. blockingReferences, "lovelace"];
            if (TrackedConfigurationContains(expected.SourceEntityId))
                blockingReferences = [.. blockingReferences, "tracked-configuration"];
            plan.Add(new(
                expected,
                currentEntityId,
                current.GetProperty("id").GetString()!,
                current.TryGetProperty("device_id", out var deviceId) ? deviceId.GetString() : null,
                disabledBy,
                related,
                blockingReferences.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }
        return plan;
    }

    private async Task WriteBackupAsync(
        IReadOnlyList<MigrationPlanItem> plan,
        string homeAssistantBackupId,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(backupPath))!;
        Directory.CreateDirectory(directory);
        var backup = new MigrationBackup(
            manifest.MigrationId,
            client.Version,
            homeAssistantBackupId,
            DateTimeOffset.UtcNow,
            plan.Select(item => new MigrationBackupEntry(
                item.Expected.ComponentId,
                item.Expected.UniqueId,
                item.Expected.SourceEntityId,
                item.Expected.TargetEntityId,
                item.CurrentEntityId,
                item.RegistryId,
                item.DeviceId,
                item.DisabledBy,
                item.Related)).ToArray());
        await using var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, backup, JsonOptions, cancellationToken);
        Console.WriteLine("Wrote rollback manifest to {0}.", backupPath);
    }

    private async Task VerifyAsync(IReadOnlyList<MigrationPlanItem> plan, bool useTargets, CancellationToken cancellationToken)
    {
        var entries = await client.ListEntitiesAsync(cancellationToken);
        foreach (var item in plan)
        {
            var current = FindByUniqueId(entries, item.Expected.UniqueId);
            var expectedEntityId = useTargets ? item.Expected.TargetEntityId : item.Expected.SourceEntityId;
            if (current.GetProperty("entity_id").GetString() != expectedEntityId)
                throw new InvalidOperationException($"Entity {item.Expected.UniqueId} did not reach {expectedEntityId}.");
            if (current.GetProperty("id").GetString() != item.RegistryId
                || (current.TryGetProperty("device_id", out var deviceId) ? deviceId.GetString() : null) != item.DeviceId
                || current.GetProperty("disabled_by").GetString() != item.DisabledBy)
                throw new InvalidOperationException($"Entity {item.Expected.UniqueId} changed registry identity, device grouping, or disabled state.");
        }
    }

    private bool TrackedConfigurationContains(string entityId)
    {
        if (!Directory.Exists(trackedConfigurationPath))
            throw new InvalidOperationException($"Tracked Home Assistant configuration was not found at {trackedConfigurationPath}.");
        return Directory.EnumerateFiles(trackedConfigurationPath, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".yaml" or ".yml" or ".json")
            .Any(path => File.ReadAllText(path).Contains(entityId, StringComparison.Ordinal));
    }

    private static JsonElement FindByUniqueId(IEnumerable<JsonElement> entries, string uniqueId)
    {
        var matches = entries.Where(entry => entry.GetProperty("unique_id").GetString() == uniqueId).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Home Assistant entity with unique ID {uniqueId} was not found."),
            _ => throw new InvalidOperationException($"Home Assistant contains duplicate unique ID {uniqueId}.")
        };
    }

    private static void PrintPlan(IReadOnlyList<MigrationPlanItem> plan)
    {
        foreach (var item in plan)
            Console.WriteLine("{0} -> {1} ({2})", item.CurrentEntityId, item.Expected.TargetEntityId,
                item.BlockingReferences.Count == 0 ? "ready" : $"blocked: {string.Join(',', item.BlockingReferences)}");
        Console.WriteLine("Validated {0} collision-free Davis entities; {1} require rename.",
            plan.Count,
            plan.Count(item => item.CurrentEntityId == item.Expected.SourceEntityId));
    }

    private static void EnsureNoBlockingReferences(IReadOnlyList<MigrationPlanItem> plan)
    {
        if (plan.Any(item => item.BlockingReferences.Count > 0))
            throw new InvalidOperationException("Migration is blocked by Home Assistant references to old entity IDs; update them and rerun the check. If --apply was used, the pre-reference backup ID is recorded in the rollback manifest.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

internal sealed record MigrationPlanItem(
    ResolvedEntityMigration Expected,
    string CurrentEntityId,
    string RegistryId,
    string? DeviceId,
    string? DisabledBy,
    JsonElement? Related,
    IReadOnlyList<string> BlockingReferences);

internal sealed record MigrationBackup(
    string MigrationId,
    string HomeAssistantVersion,
    string HomeAssistantBackupId,
    DateTimeOffset CreatedAtUtc,
    MigrationBackupEntry[] Entities);

internal sealed record MigrationBackupEntry(
    string ComponentId,
    string UniqueId,
    string SourceEntityId,
    string TargetEntityId,
    string CurrentEntityId,
    string RegistryId,
    string? DeviceId,
    string? DisabledBy,
    JsonElement? Related);

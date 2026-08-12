using System.Text.Json;
using FluentAssertions;

namespace HVO.Tools.HomeAssistantEntityMigration.Tests;

[TestClass]
public sealed class EntityMigrationRunnerTests
{
    [TestMethod]
    public async Task Check_AlreadyMigratedEntityWithOldReference_IsRejected()
    {
        var manifest = await LoadManifestAsync();
        var resolved = manifest.Resolve();
        await using var client = FakeRegistryClient.AtTargets(manifest);
        client.Related[resolved[0].SourceEntityId] = JsonSerializer.SerializeToElement(new
        {
            automation = new[] { "automation.weather_alert" }
        });
        using var files = new TestFiles();
        var runner = new EntityMigrationRunner(client, manifest, files.BackupPath, files.ConfigurationPath);

        var act = () => runner.CheckAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*references to old entity IDs*");
        client.RelatedRequests.Should().Contain(resolved[0].SourceEntityId);
    }

    [TestMethod]
    public async Task Check_AlreadyMigratedEntityWithDuplicateOldId_IsRejected()
    {
        var manifest = await LoadManifestAsync();
        var resolved = manifest.Resolve();
        await using var client = FakeRegistryClient.AtTargets(manifest);
        client.Entities.Add(new(
            "duplicate-entity",
            "duplicate-unique-id",
            resolved[0].SourceEntityId,
            "duplicate-device",
            null,
            "mqtt"));
        using var files = new TestFiles();
        var runner = new EntityMigrationRunner(client, manifest, files.BackupPath, files.ConfigurationPath);

        var act = () => runner.CheckAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*occupied by a duplicate entity*");
    }

    [TestMethod]
    public async Task Apply_PartiallyMigratedRegistry_RenamesOnlyPendingEntity()
    {
        var manifest = await LoadManifestAsync();
        var resolved = manifest.Resolve();
        await using var client = FakeRegistryClient.AtTargets(manifest);
        client.Entities.Single(entity => entity.UniqueId == resolved[0].UniqueId).EntityId = resolved[0].SourceEntityId;
        using var files = new TestFiles();
        var runner = new EntityMigrationRunner(client, manifest, files.BackupPath, files.ConfigurationPath);

        await runner.ApplyAsync(CancellationToken.None);

        client.BackupRequests.Should().Be(1);
        client.ListRequests.Should().Be(2, "apply should plan once and list once more for final verification");
        client.Renames.Should().Equal((resolved[0].SourceEntityId, resolved[0].TargetEntityId));
        client.Entities.Should().OnlyContain(entity =>
            resolved.Single(expected => expected.UniqueId == entity.UniqueId).TargetEntityId == entity.EntityId);
        File.Exists(files.BackupPath).Should().BeTrue();
    }

    [TestMethod]
    public async Task Rollback_ReverseManifest_RestoresAllSourceIds()
    {
        var manifest = await LoadManifestAsync();
        var resolved = manifest.Resolve();
        await using var client = FakeRegistryClient.AtTargets(manifest);
        using var files = new TestFiles();
        var backup = new MigrationBackup(
            manifest.MigrationId,
            manifest.HomeAssistantVersion,
            "home-assistant-backup-id",
            DateTimeOffset.UtcNow,
            resolved.Select(entity => new MigrationBackupEntry(
                entity.ComponentId,
                entity.UniqueId,
                entity.SourceEntityId,
                entity.TargetEntityId,
                entity.SourceEntityId,
                $"registry-{entity.ComponentId}",
                "davis-device",
                entity.EnabledByDefault ? null : "integration",
                null)).ToArray());
        await File.WriteAllTextAsync(files.RollbackPath, JsonSerializer.Serialize(backup, JsonOptions));
        var runner = new EntityMigrationRunner(client, manifest, files.BackupPath, files.ConfigurationPath);

        await runner.RollbackAsync(files.RollbackPath, CancellationToken.None);

        client.Renames.Should().HaveCount(39);
        client.Entities.Should().OnlyContain(entity =>
            resolved.Single(expected => expected.UniqueId == entity.UniqueId).SourceEntityId == entity.EntityId);
    }

    [TestMethod]
    public async Task Rollback_DifferentConnectedVersion_IsRejectedBeforeReadingRegistry()
    {
        var manifest = await LoadManifestAsync();
        await using var client = FakeRegistryClient.AtTargets(manifest, "different-version");
        using var files = new TestFiles();
        var runner = new EntityMigrationRunner(client, manifest, files.BackupPath, files.ConfigurationPath);

        var act = () => runner.RollbackAsync(files.RollbackPath, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not match pinned migration version*");
        client.ListRequests.Should().Be(0);
    }

    private static Task<EntityMigrationManifest> LoadManifestAsync() =>
        EntityMigrationManifest.LoadAsync(ManifestPath(), CancellationToken.None);

    private static string ManifestPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HVO.WebSite.sln")))
            directory = directory.Parent;
        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."),
            "tools",
            "HVO.Tools.HomeAssistantEntityMigration",
            "davis-readable-ids.json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class TestFiles : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"hvo-ha-migration-{Guid.NewGuid():N}");

        public TestFiles()
        {
            ConfigurationPath = Path.Combine(root, "configuration");
            BackupPath = Path.Combine(root, "artifacts", "backup.json");
            RollbackPath = Path.Combine(root, "rollback.json");
            Directory.CreateDirectory(ConfigurationPath);
            File.WriteAllText(Path.Combine(ConfigurationPath, "configuration.yaml"), "# test configuration");
        }

        public string ConfigurationPath { get; }
        public string BackupPath { get; }
        public string RollbackPath { get; }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }

    private sealed class FakeRegistryClient(string version, IEnumerable<FakeEntity> entities) : IHomeAssistantRegistryClient
    {
        public string Version { get; } = version;
        public List<FakeEntity> Entities { get; } = [.. entities];
        public Dictionary<string, JsonElement> Related { get; } = new(StringComparer.Ordinal);
        public List<string> RelatedRequests { get; } = [];
        public List<(string Source, string Target)> Renames { get; } = [];
        public int BackupRequests { get; private set; }
        public int ListRequests { get; private set; }

        public static FakeRegistryClient AtTargets(EntityMigrationManifest manifest, string? version = null) => new(
            version ?? manifest.HomeAssistantVersion,
            manifest.Resolve().Select(entity => new FakeEntity(
                $"registry-{entity.ComponentId}",
                entity.UniqueId,
                entity.TargetEntityId,
                "davis-device",
                entity.EnabledByDefault ? null : "integration",
                "mqtt")));

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<JsonElement[]> ListEntitiesAsync(CancellationToken cancellationToken)
        {
            ListRequests++;
            return Task.FromResult(Entities.Select(ToJson).ToArray());
        }

        public Task<JsonElement> FindRelatedAsync(string entityId, CancellationToken cancellationToken)
        {
            RelatedRequests.Add(entityId);
            return Task.FromResult(Related.TryGetValue(entityId, out var related)
                ? related
                : JsonSerializer.SerializeToElement(new { }));
        }

        public Task<string> CreateBackupAsync(string name, CancellationToken cancellationToken)
        {
            BackupRequests++;
            return Task.FromResult("home-assistant-backup-id");
        }

        public Task<IReadOnlyList<JsonElement>> GetLovelaceConfigurationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JsonElement>>([]);

        public Task<JsonElement> RenameAsync(string sourceEntityId, string targetEntityId, CancellationToken cancellationToken)
        {
            if (Entities.Any(entity => entity.EntityId == targetEntityId))
                throw new InvalidOperationException($"Target {targetEntityId} is occupied.");
            var entity = Entities.Single(item => item.EntityId == sourceEntityId);
            entity.EntityId = targetEntityId;
            Renames.Add((sourceEntityId, targetEntityId));
            return Task.FromResult(ToJson(entity));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static JsonElement ToJson(FakeEntity entity) => JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["id"] = entity.RegistryId,
                ["unique_id"] = entity.UniqueId,
                ["entity_id"] = entity.EntityId,
                ["device_id"] = entity.DeviceId,
                ["disabled_by"] = entity.DisabledBy,
                ["platform"] = entity.Platform
            });
    }

    private sealed record FakeEntity(
        string RegistryId,
        string UniqueId,
        string InitialEntityId,
        string? DeviceId,
        string? DisabledBy,
        string Platform)
    {
        public string EntityId { get; set; } = InitialEntityId;
    }
}

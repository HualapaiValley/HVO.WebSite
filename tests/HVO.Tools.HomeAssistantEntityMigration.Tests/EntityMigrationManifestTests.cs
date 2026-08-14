using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.HomeAssistant;
using Microsoft.Extensions.Options;

namespace HVO.Tools.HomeAssistantEntityMigration.Tests;

[TestClass]
public sealed class EntityMigrationManifestTests
{
    [TestMethod]
    public async Task DavisManifest_ResolvesAllEntitiesWithoutChangingStableIdentity()
    {
        var manifest = await EntityMigrationManifest.LoadAsync(ManifestPath(), CancellationToken.None);

        var resolved = manifest.Resolve();

        resolved.Should().HaveCount(39);
        resolved.Select(entity => entity.UniqueId).Should().OnlyHaveUniqueItems();
        resolved.Select(entity => entity.TargetEntityId).Should().OnlyHaveUniqueItems();
        resolved.Should().OnlyContain(entity => entity.TargetEntityId == $"sensor.davis_{entity.ComponentId}");
        resolved.Should().OnlyContain(entity => entity.SourceEntityId == $"sensor.{entity.UniqueId}");
        resolved.Count(entity => entity.EnabledByDefault).Should().Be(27);
    }

    [TestMethod]
    public async Task DuplicateTargetEntityId_IsRejectedBeforeHomeAssistantChanges()
    {
        var source = await File.ReadAllTextAsync(ManifestPath());
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement.Clone();
        var entities = root.GetProperty("entities").EnumerateArray()
            .Select(entity => new
            {
                componentId = entity.GetProperty("componentId").GetString(),
                targetEntityId = "sensor.davis_duplicate",
                enabledByDefault = entity.GetProperty("enabledByDefault").GetBoolean()
            }).ToArray();
        var invalid = JsonSerializer.Serialize(new
        {
            migrationId = root.GetProperty("migrationId").GetString(),
            homeAssistantVersion = root.GetProperty("homeAssistantVersion").GetString(),
            siteId = root.GetProperty("siteId").GetString(),
            gatewayId = root.GetProperty("gatewayId").GetString(),
            deviceId = root.GetProperty("deviceId").GetString(),
            entities
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var path = Path.Combine(Path.GetTempPath(), $"davis-manifest-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, invalid);
        try
        {
            var act = () => EntityMigrationManifest.LoadAsync(path, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*target entity ID collision*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task DavisManifest_MatchesCompleteProjectionContract()
    {
        var manifest = await EntityMigrationManifest.LoadAsync(ManifestPath(), CancellationToken.None);
        var capture = new CaptureProjection();
        _ = new DavisHomeAssistantProjection(
            capture,
            new EdgeRuntimeIdentity(
                "hvo-davis", "1", "instance", manifest.GatewayId, "davis-vantage-pro2", GatewayDomain.Weather,
                manifest.DeviceId, manifest.SiteId, manifest.DeviceId, "Testing", "host", "Davis"),
            Options.Create(new StationOptions { StationId = manifest.DeviceId }));

        var projected = capture.Definition!.Entities
            .Select(entity => (entity.ComponentId, entity.DefaultEntityId, entity.EnabledByDefault));
        var expected = manifest.Entities
            .Select(entity => (entity.ComponentId, (string?)entity.TargetEntityId, entity.EnabledByDefault));

        projected.Should().Contain(expected);
        projected.Should().Contain(
        [
            ("moon_phase", "sensor.davis_moon_phase", true),
            ("moon_illumination", "sensor.davis_moon_illumination", true),
            ("moonrise", "sensor.davis_moonrise", true),
            ("moonset", "sensor.davis_moonset", true)
        ]);
        projected.Should().HaveCount(43);
        capture.Definition.Entities.Count(entity => entity.EnabledByDefault).Should().Be(31);
        capture.Definition.Entities.Count(entity => !entity.EnabledByDefault).Should().Be(12);
    }

    [TestMethod]
    public async Task RollbackBackup_MissingManifestEntry_IsRejectedBeforeRename()
    {
        var manifest = await EntityMigrationManifest.LoadAsync(ManifestPath(), CancellationToken.None);
        var entries = manifest.Resolve().Select(entity => new MigrationBackupEntry(
            entity.ComponentId,
            entity.UniqueId,
            entity.SourceEntityId,
            entity.TargetEntityId,
            entity.TargetEntityId,
            $"registry-{entity.ComponentId}",
            "device",
            entity.EnabledByDefault ? null : "integration",
            null)).ToArray();
        var incomplete = new MigrationBackup(
            manifest.MigrationId,
            manifest.HomeAssistantVersion,
            "backup-id",
            DateTimeOffset.UtcNow,
            entries[..^1]);

        var act = () => EntityMigrationRunner.ValidateBackup(manifest, incomplete);

        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one entry*");
    }

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

    private sealed class CaptureProjection : IHomeAssistantMqttProjection
    {
        public HomeAssistantDeviceDefinition? Definition { get; private set; }
        public void UpsertDevice(HomeAssistantDeviceDefinition definition) => Definition = definition;
        public bool PublishCurrentState(HomeAssistantCurrentState state) => true;
        public bool RemoveDevice(HomeAssistantDeviceKey key) => false;
        public HomeAssistantMqttStatus GetStatus() => new(true, true, Definition is null ? 0 : 1, null, null, null);
    }
}

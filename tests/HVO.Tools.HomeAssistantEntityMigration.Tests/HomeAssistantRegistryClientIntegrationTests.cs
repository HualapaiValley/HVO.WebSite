using FluentAssertions;

namespace HVO.Tools.HomeAssistantEntityMigration.Tests;

[TestClass]
[TestCategory("Integration")]
[TestCategory("HomeAssistantIntegration")]
public sealed class HomeAssistantRegistryClientIntegrationTests
{
    [TestMethod]
    public async Task PinnedHomeAssistant_RenamesAndRollsBackWithoutChangingUniqueId()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_HA_TEST_URL");
        if (baseUrl is null)
            Assert.Inconclusive("Run tools/run-home-assistant-integration-tests.sh to provide the disposable environment.");
        var token = Environment.GetEnvironmentVariable("HVO_HA_TEST_TOKEN")
            ?? throw new InvalidOperationException("HVO_HA_TEST_TOKEN is required.");
        var endpoint = new Uri(baseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/api/websocket");
        const string source = "sensor.fixture_test_kasa_power";
        const string target = "sensor.fixture_test_kasa_power_migration_test";

        await using var client = new HomeAssistantRegistryClient(endpoint, token);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await client.ConnectAsync(cancellation.Token);
        client.Version.Should().Be("2026.8.1");
        var backupId = await client.CreateBackupAsync("Entity migration integration test", cancellation.Token);
        backupId.Should().NotBeNullOrWhiteSpace();
        var before = (await client.ListEntitiesAsync(cancellation.Token))
            .Single(entry => entry.GetProperty("entity_id").GetString() == source);
        var uniqueId = before.GetProperty("unique_id").GetString();

        try
        {
            await client.RenameAsync(source, target, cancellation.Token);
            var renamed = (await client.ListEntitiesAsync(cancellation.Token))
                .Single(entry => entry.GetProperty("entity_id").GetString() == target);
            renamed.GetProperty("unique_id").GetString().Should().Be(uniqueId);
        }
        finally
        {
            var entries = await client.ListEntitiesAsync(cancellation.Token);
            if (entries.Any(entry => entry.GetProperty("entity_id").GetString() == target))
                await client.RenameAsync(target, source, cancellation.Token);
        }

        var restored = (await client.ListEntitiesAsync(cancellation.Token))
            .Single(entry => entry.GetProperty("entity_id").GetString() == source);
        restored.GetProperty("unique_id").GetString().Should().Be(uniqueId);
    }
}

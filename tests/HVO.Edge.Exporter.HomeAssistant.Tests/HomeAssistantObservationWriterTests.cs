using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
public sealed class HomeAssistantObservationWriterTests
{
    [TestMethod]
    public async Task EnqueueAsync_PersistsBeforeReturnAndDeduplicatesReconciliation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hvo-ha-exporter-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "outbox.db");
        Directory.CreateDirectory(directory);
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<DefaultEdgeOutboxDbContext>(builder => builder.UseSqlite($"Data Source={databasePath}"));
            services.AddScoped<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
            await using var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>().Database.EnsureCreatedAsync();
            var writer = new HomeAssistantObservationWriter(provider.GetRequiredService<IServiceScopeFactory>());
            var timestamp = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
            var observation = new HomeAssistantMappedObservation(
                "kasa", "LoadPowerW:100", "kasa:plug-1", "plug-1", timestamp, HomeAssistantExportContract.PowerReading,
                new PowerReadingPayload
                {
                    SourceId = "kasa:plug-1",
                    SourceSystem = "homeassistant-tplink",
                    DeviceId = "plug-1",
                    RecordedAtUtc = timestamp.UtcDateTime,
                    LoadPowerW = 100
                });

            (await writer.EnqueueAsync(observation, CancellationToken.None)).Should().BeTrue();
            (await writer.EnqueueAsync(observation, CancellationToken.None)).Should().BeFalse();

            await using var verificationScope = provider.CreateAsyncScope();
            var records = await verificationScope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>()
                .OutboxRecords.AsNoTracking().ToListAsync();
            records.Should().ContainSingle();
            records[0].PayloadType.Should().Be(HVO.Edge.Contracts.EdgePayloadTypes.HomeAssistantObservation);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}

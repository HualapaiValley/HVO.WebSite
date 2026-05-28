using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class GatewayStatusSnapshotWorkerTests
{
    [TestMethod]
    public async Task QueueOnceAsync_EnqueuesGatewayStatusPayload()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var observedAt = DateTime.Parse("2026-05-28T07:45:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(conn));
        services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
        services.AddScoped<PowerInventoryConfigurationWriter>();
        services.AddSingleton<IGatewayStatusPayloadProvider>(new StaticGatewayStatusPayloadProvider(new GatewayStatusPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = observedAt,
            Identity = new GatewayIdentity("solarassistant", "SolarAssistant Gateway", GatewayDomain.Power, "solarassistant-total", "total"),
            Health = new GatewayHealthSnapshot(GatewayHealthState.Healthy, observedAt, [], GatewaySampleState.Live),
            Rest = new GatewayRuntimeSignal(GatewaySampleState.Live, observedAt, Detail: "124 REST metric(s)"),
            Outbox = new GatewayOutboxStatus(PendingCount: 0, FailedCount: 0),
            RestMetricCount = 124,
        }));
        services.AddSingleton<IOptions<SolarAssistantOptions>>(Options.Create(new SolarAssistantOptions
        {
            Host = "solarassistant.local",
            GatewayStatusIntervalSeconds = 60,
        }));
        services.AddSingleton(sp => new GatewayStatusSnapshotWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IGatewayStatusPayloadProvider>(),
            sp.GetRequiredService<IOptions<SolarAssistantOptions>>(),
            NullLogger<GatewayStatusSnapshotWorker>.Instance));

        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();

        var inserted = await provider.GetRequiredService<GatewayStatusSnapshotWorker>().QueueOnceAsync(CancellationToken.None);

        inserted.Should().BeTrue();
        using var verifyScope = provider.CreateScope();
        var row = verifyScope.ServiceProvider.GetRequiredService<OutboxDbContext>().OutboxRecords.Single();
        row.PayloadType.Should().Be(PowerOutboxPayloadTypes.GatewayStatus);
        row.PayloadVersion.Should().Be(PowerOutboxPayloadTypes.GatewayStatusVersion);
        row.SourceId.Should().Be("solarassistant-total");
        row.PayloadJson.Should().Contain("SolarAssistant Gateway");
    }

    private sealed class StaticGatewayStatusPayloadProvider(GatewayStatusPayload payload) : IGatewayStatusPayloadProvider
    {
        public GatewayStatusPayload CreatePayload(DateTime? nowUtc = null) => payload;
    }
}

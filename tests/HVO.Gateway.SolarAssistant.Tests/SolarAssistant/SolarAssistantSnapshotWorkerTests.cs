using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantSnapshotWorkerTests
{
    private SqliteConnection _conn = null!;
    private ServiceProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(_conn));
        services.AddScoped<PowerOutboxWriter>();
        services.AddSingleton<IOptions<SolarAssistantOptions>>(Options.Create(new SolarAssistantOptions
        {
            Host = "solarassistant.local",
            TotalSourceId = "solarassistant-total",
            TotalDeviceId = "total",
        }));
        services.AddSingleton<ISolarAssistantClient>(_ => new FakeSolarAssistantClient([
            new SolarAssistantMetric { Topic = "total/pv_power", Value = 1234 },
            new SolarAssistantMetric { Topic = "total/load_power", Value = 567 },
        ]));
        services.AddSingleton(sp => new SolarAssistantSnapshotWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ISolarAssistantClient>(),
            sp.GetRequiredService<IOptions<SolarAssistantOptions>>(),
            NullLogger<SolarAssistantSnapshotWorker>.Instance));

        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
        _conn.Dispose();
    }

    [TestMethod]
    public async Task PollOnceAsync_QueuesMappedPowerSnapshot()
    {
        var worker = _provider.GetRequiredService<SolarAssistantSnapshotWorker>();

        var queued = await worker.PollOnceAsync(CancellationToken.None);

        queued.Should().BeTrue();
        worker.LastMetricCount.Should().Be(2);
        worker.LastSnapshotAt.Should().NotBeNull();
        worker.LastError.Should().BeNull();
        worker.LastSnapshot.Should().NotBeNull();
        worker.LastSnapshot!.SourceId.Should().Be("solarassistant-total");
        worker.LastSnapshot.PvPowerW.Should().Be(1234);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = db.OutboxRecords.Single();
        row.SourceId.Should().Be("solarassistant-total");
        row.Payload.Should().Contain("1234");
        row.Payload.Should().Contain("pvPowerW");
    }

    private sealed class FakeSolarAssistantClient : ISolarAssistantClient
    {
        private readonly IReadOnlyList<SolarAssistantMetric> _metrics;

        public FakeSolarAssistantClient(IReadOnlyList<SolarAssistantMetric> metrics)
        {
            _metrics = metrics;
        }

        public Task<IReadOnlyList<SolarAssistantMetric>> GetMetricsAsync(CancellationToken ct) => Task.FromResult(_metrics);
    }
}

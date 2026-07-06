using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.Telemetry;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Tests.Workers;

[TestClass]
public sealed class SmartShuntWorkerTests
{
    [TestMethod]
    public async Task PollOnceAsync_ReturnsFalseWhenNoCurrentSample()
    {
        await using var fixture = await WorkerFixture.CreateAsync();

        var result = await fixture.Worker.PollOnceAsync(CancellationToken.None);

        result.Should().BeFalse();
        fixture.Worker.LastSnapshot.Should().BeNull();
        fixture.Db.OutboxRecords.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PollOnceAsync_NormalizesDefaultTimestampToUtc()
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        fixture.Session.CurrentSample = Sample(recordedAtUtc: default);

        var before = DateTime.UtcNow;
        var result = await fixture.Worker.PollOnceAsync(CancellationToken.None);
        var after = DateTime.UtcNow;

        result.Should().BeTrue();
        fixture.Worker.LastSnapshotAt.Should().NotBeNull();
        fixture.Worker.LastSnapshotAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        fixture.Worker.LastSnapshotAt.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        fixture.Worker.LastSnapshot!.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [TestMethod]
    public async Task PollOnceAsync_AppliesFreshPrivateOverlay()
    {
        var recordedAt = Utc(2026, 6, 17, 12, 0, 0);
        await using var fixture = await WorkerFixture.CreateAsync(new SmartShuntOptions
        {
            SourceId = "smartshunt-main",
            DeviceId = "battery",
            EnablePrivateEnrichment = true,
            PrivateRefreshIntervalSeconds = 1,
        });
        fixture.Session.CurrentSample = Sample(recordedAt, stateOfChargePercent: 0, remainingMinutes: null);
        fixture.PrivateInfo.Next = new SmartShuntDeviceInfo
        {
            Overlay = new SmartShuntPrivateOverlay
            {
                RecordedAtUtc = recordedAt,
                StateOfChargePercent = 92.5,
                RemainingMinutes = 180,
                TotalChargeCycles = 42,
            },
        };

        var result = await fixture.Worker.PollOnceAsync(CancellationToken.None);

        result.Should().BeTrue();
        fixture.PrivateInfo.ReadCount.Should().Be(1);
        fixture.Worker.LastSnapshot!.DataPath.Should().Be("public+private");
        fixture.Worker.LastSnapshot.StateOfChargePercent.Should().Be(92.5);
        fixture.Worker.LastSnapshot.RemainingMinutes.Should().Be(180);
        fixture.Worker.LastSnapshot.TotalChargeCycles.Should().Be(42);
    }

    [TestMethod]
    public async Task PollOnceAsync_WritesOutboxOnSnapshotCadence()
    {
        var first = Utc(2026, 6, 17, 12, 0, 0);
        await using var fixture = await WorkerFixture.CreateAsync(new SmartShuntOptions
        {
            SourceId = "smartshunt-main",
            DeviceId = "battery",
            SnapshotIntervalSeconds = 60,
        });

        fixture.Session.CurrentSample = Sample(first, voltageV: 52.1);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);
        fixture.Session.CurrentSample = Sample(first.AddSeconds(30), voltageV: 52.2);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);
        fixture.Session.CurrentSample = Sample(first.AddSeconds(60), voltageV: 52.3);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);

        var rows = fixture.Db.OutboxRecords.OrderBy(r => r.RecordedAtUtc).ToList();
        rows.Should().HaveCount(2);
        rows.Select(r => r.RecordedAtUtc).Should().Equal(first, first.AddSeconds(60));
        rows.Should().OnlyContain(r => r.PayloadType == SmartShuntOutboxPayloadTypes.Reading);
        rows.Last().PayloadJson.Should().Contain("52.3");
    }

    [TestMethod]
    public async Task PollOnceAsync_TrimsHistoryToCapacity()
    {
        var first = Utc(2026, 6, 17, 12, 0, 0);
        await using var fixture = await WorkerFixture.CreateAsync(new SmartShuntOptions
        {
            SourceId = "smartshunt-main",
            DeviceId = "battery",
            HistoryCapacity = 2,
        });

        fixture.Session.CurrentSample = Sample(first, powerW: -100);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);
        fixture.Session.CurrentSample = Sample(first.AddSeconds(1), powerW: -200);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);
        fixture.Session.CurrentSample = Sample(first.AddSeconds(2), powerW: -300);
        await fixture.Worker.PollOnceAsync(CancellationToken.None);

        fixture.Worker.History.Should().HaveCount(2);
        fixture.Worker.History.Select(p => p.PowerW).Should().Equal(-200, -300);
    }

    private static SmartShuntLiveSample Sample(
        DateTime recordedAtUtc,
        double? stateOfChargePercent = 75,
        double? voltageV = 53.2,
        double? currentA = -12.5,
        double? powerW = -665,
        double? remainingMinutes = 240) => new()
        {
            RecordedAtUtc = recordedAtUtc,
            StateOfChargePercent = stateOfChargePercent,
            VoltageV = voltageV,
            CurrentA = currentA,
            PowerW = powerW,
            ConsumedAh = -50,
            RemainingMinutes = remainingMinutes,
            PublicSessionActive = true,
            DataPath = "public",
        };

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private WorkerFixture(SqliteConnection connection, ServiceProvider provider, FakeSessionState session, FakePrivateInfoSource privateInfo)
        {
            _connection = connection;
            _provider = provider;
            Session = session;
            PrivateInfo = privateInfo;
            Db = provider.GetRequiredService<OutboxDbContext>();
            Worker = provider.GetRequiredService<SmartShuntWorker>();
        }

        public FakeSessionState Session { get; }
        public FakePrivateInfoSource PrivateInfo { get; }
        public OutboxDbContext Db { get; }
        public SmartShuntWorker Worker { get; }

        public static async Task<WorkerFixture> CreateAsync(SmartShuntOptions? options = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var session = new FakeSessionState();
            var privateInfo = new FakePrivateInfoSource();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OutboxDbContext>(builder => builder.UseSqlite(connection));
            services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
            services.AddScoped<PowerOutboxWriter>();
            services.AddSingleton<ISmartShuntSessionState>(session);
            services.AddSingleton<ISmartShuntPrivateInfoSource>(privateInfo);
            services.AddSingleton<IOptions<SmartShuntOptions>>(Options.Create(options ?? new SmartShuntOptions
            {
                SourceId = "smartshunt-main",
                DeviceId = "battery",
            }));
            services.AddSingleton(sp => new SmartShuntWorker(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ISmartShuntSessionState>(),
                sp.GetRequiredService<ISmartShuntPrivateInfoSource>(),
                sp.GetRequiredService<IOptions<SmartShuntOptions>>(),
                NullLogger<SmartShuntWorker>.Instance,
                new SmartShuntTelemetry()));
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<OutboxDbContext>();
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                db,
                SmartShuntOutboxPayloadTypes.Reading,
                SmartShuntOutboxPayloadTypes.ReadingVersion);
            return new WorkerFixture(connection, provider, session, privateInfo);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeSessionState : ISmartShuntSessionState
    {
        public SmartShuntLiveSample? CurrentSample { get; set; }
    }

    private sealed class FakePrivateInfoSource : ISmartShuntPrivateInfoSource
    {
        public SmartShuntDeviceInfo? Next { get; set; }
        public int ReadCount { get; private set; }

        public Task<SmartShuntDeviceInfo?> TryReadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(Next);
        }
    }
}

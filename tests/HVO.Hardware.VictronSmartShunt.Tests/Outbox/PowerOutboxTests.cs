using System.Net;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Tests.Outbox;

[TestClass]
public sealed class PowerOutboxTests
{
    [TestMethod]
    public async Task EnqueueAsync_SerializesPayloadAndSkipsDuplicateSourceTimestamp()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var writer = new PowerOutboxWriter(fixture.Store, NullLogger<PowerOutboxWriter>.Instance);
        var payload = CreatePayload(sourceId: " smartshunt-main ");

        var first = await writer.EnqueueAsync(payload, CancellationToken.None);
        var second = await writer.EnqueueAsync(payload, CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
        var record = fixture.Db.OutboxRecords.Should().ContainSingle().Subject;
        record.SourceId.Should().Be("smartshunt-main");
        record.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        record.PayloadType.Should().Be(SmartShuntOutboxPayloadTypes.Reading);
        record.PayloadVersion.Should().Be(SmartShuntOutboxPayloadTypes.ReadingVersion);
        using var json = JsonDocument.Parse(record.PayloadJson);
        json.RootElement.GetProperty("sourceId").GetString().Should().Be(" smartshunt-main ");
        json.RootElement.GetProperty("batteryVoltageV").GetDouble().Should().Be(53.42);
        json.RootElement.GetProperty("batteryCurrentA").GetDouble().Should().Be(-12.5);
    }

    [TestMethod]
    public async Task SweepAsync_InvalidPayloadMarksRecordFailedWithoutHttpCall()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Db.OutboxRecords.Add(new EdgeOutboxRecord
        {
            SourceId = "smartshunt-main",
            RecordedAtUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            PayloadType = SmartShuntOutboxPayloadTypes.Reading,
            PayloadVersion = SmartShuntOutboxPayloadTypes.ReadingVersion,
            PayloadJson = "not json",
            Status = EdgeOutboxStatus.Pending,
        });
        await fixture.Db.SaveChangesAsync();

        var calls = 0;
        var forwarder = new PowerApiForwarder(
            fixture.ScopeFactory,
            new StubHttpClientFactory(new HttpClient(new StubHandler(_ =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }))),
            Options.Create(new OutboxOptions
            {
                ApiEndpoint = "https://example.test/api/v1/power/readings",
                ApiKey = "test-api-key",
                BatchSize = 10,
            }),
            new RuntimeOutboxSettings(), NullLogger<PowerApiForwarder>.Instance);

        await forwarder.SweepAsync(CancellationToken.None);

        calls.Should().Be(0);
        fixture.Db.ChangeTracker.Clear();
        var record = fixture.Db.OutboxRecords.Single();
        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
        record.LastError.Should().Be("Outbox payload JSON is invalid.");
        forwarder.FailedCount.Should().Be(1);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow(HttpStatusCode.NotFound)]
    public async Task SweepAsync_AuthAndEndpointFailuresAreDeadLettered(HttpStatusCode statusCode)
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var payload = CreatePayload();
        await new PowerOutboxWriter(fixture.Store, NullLogger<PowerOutboxWriter>.Instance)
            .EnqueueAsync(payload, CancellationToken.None);

        var forwarder = new PowerApiForwarder(
            fixture.ScopeFactory,
            new StubHttpClientFactory(new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("configuration mismatch"),
            }))),
            Options.Create(new OutboxOptions
            {
                ApiEndpoint = "https://example.test/api/v1/power/readings",
                ApiKey = "test-api-key",
                BatchSize = 10,
                MaxRetryAttempts = 10,
                MaxBackoffSeconds = 300,
            }),
            new RuntimeOutboxSettings(), NullLogger<PowerApiForwarder>.Instance);

        await forwarder.SweepAsync(CancellationToken.None);

        fixture.Db.ChangeTracker.Clear();
        var record = fixture.Db.OutboxRecords.Single();
        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
        record.AttemptCount.Should().Be(1);
        record.LastError.Should().Contain($"HTTP {(int)statusCode}");
    }

    [TestMethod]
    public async Task RequeueRetryExhaustedAsync_RequeuesWithoutFreshSentRecords()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        fixture.Db.OutboxRecords.Add(new EdgeOutboxRecord
        {
            SourceId = "smartshunt-main",
            RecordedAtUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            PayloadType = SmartShuntOutboxPayloadTypes.Reading,
            PayloadVersion = SmartShuntOutboxPayloadTypes.ReadingVersion,
            PayloadJson = JsonSerializer.Serialize(CreatePayload()),
            Status = EdgeOutboxStatus.Failed,
            FailureKind = EdgeOutboxFailureKind.RetryExhausted,
            AttemptCount = 10,
            LastError = "offline",
        });
        await fixture.Db.SaveChangesAsync();

        var forwarder = new PowerApiForwarder(
            fixture.ScopeFactory,
            new StubHttpClientFactory(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)))),
            Options.Create(new OutboxOptions
            {
                ApiEndpoint = "https://example.test/api/v1/power/readings",
                ApiKey = "test-api-key",
            }),
            new RuntimeOutboxSettings(), NullLogger<PowerApiForwarder>.Instance);

        await forwarder.RequeueRetryExhaustedAsync(CancellationToken.None);

        fixture.Db.ChangeTracker.Clear();
        var record = fixture.Db.OutboxRecords.Single();
        record.Status.Should().Be(EdgeOutboxStatus.Pending);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.None);
        record.AttemptCount.Should().Be(0);
        record.LastError.Should().Contain("Requeued after retry exhaustion");
    }

    private static PowerReadingPayload CreatePayload(string sourceId = "smartshunt-main") => new()
    {
        SourceId = sourceId,
        SourceSystem = "victron-smartshunt",
        DeviceId = "main",
        RecordedAtUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        BatteryPowerW = -667.75,
        BatteryStateOfChargePercent = 82.4,
        BatteryVoltageV = 53.42,
        BatteryCurrentA = -12.5,
        BatteryCapacityKwh = 14.3,
    };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class OutboxFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;

        private OutboxFixture(SqliteConnection connection, ServiceProvider serviceProvider, OutboxDbContext db)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
            Db = db;
            ScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        }

        public OutboxDbContext Db { get; }
        public EdgeOutboxStore<OutboxDbContext> Store => _serviceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>();
        public IServiceScopeFactory ScopeFactory { get; }

        public static async Task<OutboxFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<OutboxDbContext>();
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                db,
                SmartShuntOutboxPayloadTypes.Reading,
                SmartShuntOutboxPayloadTypes.ReadingVersion);

            return new OutboxFixture(connection, provider, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _serviceProvider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

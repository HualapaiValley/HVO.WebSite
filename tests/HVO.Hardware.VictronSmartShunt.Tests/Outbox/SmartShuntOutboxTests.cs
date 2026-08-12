using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Tests.Outbox;

[TestClass]
public sealed class SmartShuntOutboxTests
{
    private static readonly DateTime RecordedAt = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Writer_PersistsOneTypedBundleAndDeduplicates()
    {
        var options = new DbContextOptionsBuilder<DefaultEdgeOutboxDbContext>().UseSqlite("Data Source=:memory:").Options;
        await using var db = new DefaultEdgeOutboxDbContext(options);
        await db.Database.OpenConnectionAsync();
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.SmartShuntObservation, "1");
        var writer = new SmartShuntOutboxWriter(new EdgeOutboxStore<DefaultEdgeOutboxDbContext>(db), NullLogger<SmartShuntOutboxWriter>.Instance);

        (await writer.EnqueueAsync(Bundle(), CancellationToken.None)).Should().BeTrue();
        (await writer.EnqueueAsync(Bundle(), CancellationToken.None)).Should().BeFalse();
        var row = await db.OutboxRecords.SingleAsync();
        row.PayloadType.Should().Be(EdgePayloadTypes.SmartShuntObservation);
        JsonSerializer.Deserialize<SmartShuntObservationPayload>(row.PayloadJson, JsonSerializerOptions.Web)!.Detail.ConsumedAh.Should().Be(-25);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    public async Task Sender_TreatsAuthenticationFailuresAsRecoverable(HttpStatusCode status)
    {
        var outcome = await Sender(new StubHandler(_ => new(status))).SendAsync([Record()], CancellationToken.None);
        outcome.Single().Status.Should().Be(EdgeOutboxSendStatus.TransientFailure);
    }

    [TestMethod]
    public async Task Sender_RequiresStrictAccountingAndSendsDetailAfterAcceptedSummary()
    {
        var handler = new StubHandler(request => new(HttpStatusCode.Created)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("readings", StringComparison.Ordinal)
                ? "{\"inserted\":1,\"skipped\":0,\"failed\":[]}" : "{}", Encoding.UTF8, "application/json")
        });
        var outcome = await Sender(handler).SendAsync([Record()], CancellationToken.None);
        outcome.Single().Status.Should().Be(EdgeOutboxSendStatus.TransientFailure);
        handler.Paths.Should().Equal("/api/v1/power/smartshunt-observations/batch");
    }

    [TestMethod]
    public async Task LegacyMigration_PreservesPendingSummaryInSharedSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"smartshunt-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DefaultEdgeOutboxDbContext>().UseSqlite($"Data Source={path}").Options;
            await using var db = new DefaultEdgeOutboxDbContext(options);
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.Legacy.SmartShuntReading, "1");
            db.OutboxRecords.Add(new() { SourceId = "source", DeviceId = "device", RecordedAtUtc = RecordedAt, PayloadType = EdgePayloadTypes.Legacy.SmartShuntReading, PayloadVersion = "1", PayloadJson = JsonSerializer.Serialize(Bundle().Summary, JsonSerializerOptions.Web) });
            await db.SaveChangesAsync();
            (await SmartShuntLegacyOutboxMigrator.MigrateAsync(db)).Should().Be(1);
            var row = await db.OutboxRecords.SingleAsync();
            row.PayloadType.Should().Be(EdgePayloadTypes.SmartShuntObservation);
            row.Status.Should().Be(EdgeOutboxStatus.Pending);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [TestMethod]
    public async Task LegacyMigration_CollapsesCollisionsWithPendingDeliveryPrecedenceAndRetainsValidBundle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"smartshunt-collision-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DefaultEdgeOutboxDbContext>().UseSqlite($"Data Source={path}").Options;
            await using var db = new DefaultEdgeOutboxDbContext(options);
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.Legacy.SmartShuntReading, "1");
            db.OutboxRecords.AddRange(
                new() { SourceId = "source", DeviceId = "device", RecordedAtUtc = RecordedAt, PayloadType = EdgePayloadTypes.Legacy.SmartShuntReading, PayloadVersion = "1", PayloadJson = "{", Status = EdgeOutboxStatus.Sent, SentAtUtc = RecordedAt },
                new() { SourceId = "source", DeviceId = "device", RecordedAtUtc = RecordedAt, PayloadType = EdgePayloadTypes.SmartShuntReading, PayloadVersion = "1", PayloadJson = JsonSerializer.Serialize(Bundle().Summary, JsonSerializerOptions.Web), Status = EdgeOutboxStatus.Pending });
            await db.SaveChangesAsync();

            await SmartShuntLegacyOutboxMigrator.MigrateAsync(db);
            await SmartShuntLegacyOutboxMigrator.MigrateAsync(db);

            var retained = await db.OutboxRecords.SingleAsync();
            retained.Status.Should().Be(EdgeOutboxStatus.Pending);
            retained.PayloadType.Should().Be(EdgePayloadTypes.SmartShuntObservation);
            JsonSerializer.Deserialize<SmartShuntObservationPayload>(retained.PayloadJson, JsonSerializerOptions.Web).Should().NotBeNull();
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static SmartShuntOutboxBatchSender Sender(StubHandler handler) => new(new Factory(handler), new SmartShuntCentralIngestCredential { ApiKey = "key" }, Options.Create(new SmartShuntOptions { CentralIngestBaseEndpoint = "https://central.test/" }));
    private static SmartShuntObservationPayload Bundle() => new(new() { SourceId = "source", SourceSystem = "victron-smartshunt", DeviceId = "device", RecordedAtUtc = RecordedAt, BatteryVoltageV = 50, BatteryCurrentA = -5, BatteryPowerW = -250, BatteryStateOfChargePercent = 80 }, new() { SourceId = "source", SourceSystem = "victron-smartshunt", DeviceId = "device", RecordedAtUtc = RecordedAt, ConsumedAh = -25, RemainingMinutes = 90 });
    private static EdgeOutboxRecord Record() => new() { Id = 1, SourceId = "source", DeviceId = "device", RecordedAtUtc = RecordedAt, PayloadType = EdgePayloadTypes.SmartShuntObservation, PayloadVersion = "1", PayloadJson = JsonSerializer.Serialize(Bundle(), JsonSerializerOptions.Web) };
    private sealed class Factory(StubHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Paths.Add(request.RequestUri!.AbsolutePath); return Task.FromResult(response(request)); }
    }
}

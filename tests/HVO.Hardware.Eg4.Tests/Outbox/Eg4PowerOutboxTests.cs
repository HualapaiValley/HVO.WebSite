using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Tests.Outbox;

[TestClass]
public sealed class Eg4PowerOutboxTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly DateTime RecordedAt = new(2026, 8, 9, 19, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task Writer_PersistsOneCompleteBundleAndDeduplicatesSourceTimestamp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eg4-outbox-{Guid.NewGuid():N}.db");
        try
        {
            var dbOptions = new DbContextOptionsBuilder<DefaultEdgeOutboxDbContext>()
                .UseSqlite($"Data Source={path}").Options;
            await using var db = new DefaultEdgeOutboxDbContext(dbOptions);
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.Eg4Observation, "1");
            var writer = new PowerOutboxWriter(
                new EdgeOutboxStore<DefaultEdgeOutboxDbContext>(db),
                NullLogger<PowerOutboxWriter>.Instance);
            var bundle = Bundle();

            (await writer.EnqueueAsync(bundle, CancellationToken.None)).Should().BeTrue();
            (await writer.EnqueueAsync(bundle, CancellationToken.None)).Should().BeFalse();

            var record = await db.OutboxRecords.SingleAsync();
            record.PayloadType.Should().Be(EdgePayloadTypes.Eg4Observation);
            record.SourceId.Should().Be("eg4-a");
            var persisted = JsonSerializer.Deserialize<Eg4ObservationBundle>(record.PayloadJson, JsonOptions);
            persisted.Should().NotBeNull();
            persisted!.Reading.BatteryCurrentA.Should().Be(-10);
            persisted.MpptDetail.Should().NotBeNull();
            persisted.InverterDetail.Should().NotBeNull();
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [TestMethod]
    public async Task Sender_BoundsDeliveryToThreeBatchRequestsPerSweep()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"inserted\":2,\"skipped\":0,\"failed\":[]}", Encoding.UTF8, "application/json")
        });
        var sender = Sender(handler);

        var outcomes = await sender.SendAsync([
            Record(Bundle(), 42),
            Record(Bundle("eg4-b", "b", RecordedAt.AddSeconds(1)), 43)
        ], CancellationToken.None);

        outcomes.Should().HaveCount(2).And.OnlyContain(outcome => outcome.Status == EdgeOutboxSendStatus.Sent);
        handler.Paths.Should().Equal(
            "/api/v1/power/readings",
            "/api/v1/power/mppt-detail/batch",
            "/api/v1/power/inverter-detail/batch");
        handler.Bodies.Should().HaveCount(3);
        handler.Bodies.Should().OnlyContain(body => BatchLength(body) == 2);
        handler.ApiKeys.Should().OnlyContain(static key => key == "central-key");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, EdgeOutboxSendStatus.PermanentFailure)]
    [DataRow(HttpStatusCode.Unauthorized, EdgeOutboxSendStatus.PermanentFailure)]
    [DataRow(HttpStatusCode.TooManyRequests, EdgeOutboxSendStatus.TransientFailure)]
    [DataRow(HttpStatusCode.ServiceUnavailable, EdgeOutboxSendStatus.TransientFailure)]
    public async Task Sender_ClassifiesCentralFailures(HttpStatusCode status, EdgeOutboxSendStatus expected)
    {
        var sender = Sender(new StubHandler(_ => new HttpResponseMessage(status)));

        var outcomes = await sender.SendAsync([Record(Bundle() with { MpptDetail = null, InverterDetail = null })], CancellationToken.None);

        outcomes.Should().ContainSingle().Which.Status.Should().Be(expected);
    }

    [TestMethod]
    public async Task Sender_ForwardsDetailsOnlyForReadingsAcceptedByCentralIngest()
    {
        var rejectedAt = RecordedAt.AddSeconds(1);
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("/readings", StringComparison.Ordinal)
                    ? $"{{\"inserted\":1,\"skipped\":0,\"failed\":[{{\"sourceId\":\"eg4-b\",\"recordedAtUtc\":\"{rejectedAt:O}\",\"error\":\"rejected\"}}]}}"
                    : "{\"inserted\":1,\"skipped\":0,\"failed\":[]}",
                Encoding.UTF8,
                "application/json")
        });

        var outcomes = await Sender(handler).SendAsync([
            Record(Bundle(), 42),
            Record(Bundle("eg4-b", "b", rejectedAt), 43)
        ], CancellationToken.None);

        outcomes.Single(outcome => outcome.RecordId == 42).Status.Should().Be(EdgeOutboxSendStatus.Sent);
        outcomes.Single(outcome => outcome.RecordId == 43).Status.Should().Be(EdgeOutboxSendStatus.PermanentFailure);
        handler.Bodies.Should().HaveCount(3);
        BatchLength(handler.Bodies[0]).Should().Be(2);
        BatchLength(handler.Bodies[1]).Should().Be(1);
        BatchLength(handler.Bodies[2]).Should().Be(1);
    }

    [TestMethod]
    public async Task Sender_RejectsMalformedBundleAndRetriesIncompleteSuccessBody()
    {
        var malformed = Record(Bundle());
        malformed.PayloadJson = "{";
        var malformedOutcome = await Sender(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)))
            .SendAsync([malformed], CancellationToken.None);
        malformedOutcome.Single().Status.Should().Be(EdgeOutboxSendStatus.PermanentFailure);

        var incomplete = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var incompleteOutcome = await Sender(incomplete)
            .SendAsync([Record(Bundle() with { MpptDetail = null, InverterDetail = null })], CancellationToken.None);
        incompleteOutcome.Single().Status.Should().Be(EdgeOutboxSendStatus.TransientFailure);
    }

    private static Eg4OutboxBatchSender Sender(StubHandler handler) => new(
        new StubHttpClientFactory(handler),
        new Eg4CentralIngestCredential { ApiKey = "central-key" },
        Options.Create(new Eg4Options { CentralIngestEndpoint = "https://central.test/" }));

    private static int BatchLength(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetArrayLength();
    }

    private static EdgeOutboxRecord Record(Eg4ObservationBundle bundle, long id = 42) => new()
    {
        Id = id,
        SourceId = bundle.Reading.SourceId!,
        DeviceId = bundle.Reading.DeviceId,
        RecordedAtUtc = bundle.Reading.RecordedAtUtc,
        PayloadType = EdgePayloadTypes.Eg4Observation,
        PayloadVersion = "1",
        PayloadJson = JsonSerializer.Serialize(bundle, JsonOptions)
    };

    private static Eg4ObservationBundle Bundle(
        string sourceId = "eg4-a",
        string deviceId = "a",
        DateTime? recordedAt = null) => new(
        new PowerReadingPayload
        {
            SourceId = sourceId,
            SourceSystem = "eg4-6500ex",
            DeviceId = deviceId,
            RecordedAtUtc = recordedAt ?? RecordedAt,
            BatteryVoltageV = 54.4,
            BatteryCurrentA = -10,
            BatteryPowerW = -544
        },
        new PowerMpptDetailPayload
        {
            SourceId = sourceId,
            SourceSystem = "eg4-6500ex",
            DeviceId = deviceId,
            RecordedAtUtc = recordedAt ?? RecordedAt,
            Trackers = [new() { TrackerId = "mppt-1", Name = "MPPT 1", PowerW = 800 }]
        },
        new PowerInverterDetailPayload
        {
            SourceId = sourceId,
            SourceSystem = "eg4-6500ex",
            DeviceId = deviceId,
            RecordedAtUtc = recordedAt ?? RecordedAt,
            TemperatureC = 50
        });

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string?> ApiKeys { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            ApiKeys.Add(request.Headers.GetValues("X-Api-Key").SingleOrDefault());
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return response(request);
        }
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

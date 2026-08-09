using System.Net;
using System.Text;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Hosting.Telemetry;
using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Tests.Outbox;

[TestClass]
public sealed class Eg4PowerOutboxTests
{
    [TestMethod]
    public async Task Writer_DeduplicatesPerSourceAndPreservesDifferentDevicesAtSameTime()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        var time = new DateTime(2026, 8, 9, 19, 0, 0, DateTimeKind.Utc);
        var first = Payload("eg4-a", "a", time);
        var second = Payload("eg4-b", "b", time);

        (await fixture.Writer.EnqueueAsync(first, CancellationToken.None)).Should().BeTrue();
        (await fixture.Writer.EnqueueAsync(first, CancellationToken.None)).Should().BeFalse();
        (await fixture.Writer.EnqueueAsync(second, CancellationToken.None)).Should().BeTrue();

        await using var db = fixture.CreateDb();
        var records = await db.OutboxRecords.OrderBy(record => record.SourceId).ToListAsync();
        records.Should().HaveCount(2);
        records.Select(record => record.SourceId).Should().Equal("eg4-a", "eg4-b");
        records.Should().OnlyContain(record =>
            (record.DeviceId == "a" || record.DeviceId == "b") && record.PayloadType == Eg4OutboxPayloadTypes.Reading);
    }

    [TestMethod]
    [DataRow(201, 1, 0)]
    [DataRow(500, 0, 1)]
    [DataRow(401, 0, 1)]
    [DataRow(400, 0, 0)]
    public async Task Forwarder_ClassifiesSuccessTransientAndPermanentResponses(int statusCode, int sent, int pending)
    {
        await using var fixture = await OutboxFixture.CreateAsync((HttpStatusCode)statusCode);
        await fixture.Writer.EnqueueAsync(Payload("eg4-a", "a", fixture.Now), CancellationToken.None);

        (await fixture.Forwarder.SweepAsync(CancellationToken.None)).Should().Be(statusCode == 201);

        await using var db = fixture.CreateDb();
        (await db.OutboxRecords.CountAsync(record => record.Status == EdgeOutboxStatus.Sent)).Should().Be(sent);
        (await db.OutboxRecords.CountAsync(record => record.Status == EdgeOutboxStatus.Pending)).Should().Be(pending);
        (await db.OutboxRecords.CountAsync(record => record.Status == EdgeOutboxStatus.Failed)).Should().Be(statusCode == 400 ? 1 : 0);
        if (statusCode == 201)
            fixture.Handler.RequestBody.Should().Contain("com.hvo.power.reading.v1").And.Contain("eg4-6500ex");
    }

    [TestMethod]
    public async Task Forwarder_RetriesIncompleteSuccessfulResponseInsteadOfAcknowledgingRecords()
    {
        await using var fixture = await OutboxFixture.CreateAsync(HttpStatusCode.Created, "{}");
        await fixture.Writer.EnqueueAsync(Payload("eg4-a", "a", fixture.Now), CancellationToken.None);

        (await fixture.Forwarder.SweepAsync(CancellationToken.None)).Should().BeFalse();

        await using var db = fixture.CreateDb();
        var record = await db.OutboxRecords.SingleAsync();
        record.Status.Should().Be(EdgeOutboxStatus.Pending);
        record.AttemptCount.Should().Be(1);
        record.LastError.Should().Be("Power API response was invalid");
    }

    [TestMethod]
    public async Task Forwarder_AppliesPartialValidationFailureOnlyToMatchingSourceAndTime()
    {
        const string response = "{\"inserted\":1,\"skipped\":0,\"failed\":[{\"sourceId\":\"eg4-b\",\"recordedAtUtc\":\"2026-08-09T19:00:00Z\"}]}";
        await using var fixture = await OutboxFixture.CreateAsync(HttpStatusCode.Created, response);
        await fixture.Writer.EnqueueAsync(Payload("eg4-a", "a", fixture.Now), CancellationToken.None);
        await fixture.Writer.EnqueueAsync(Payload("eg4-b", "b", fixture.Now), CancellationToken.None);

        (await fixture.Forwarder.SweepAsync(CancellationToken.None)).Should().BeTrue();

        await using var db = fixture.CreateDb();
        (await db.OutboxRecords.SingleAsync(record => record.SourceId == "eg4-a")).Status.Should().Be(EdgeOutboxStatus.Sent);
        var rejected = await db.OutboxRecords.SingleAsync(record => record.SourceId == "eg4-b");
        rejected.Status.Should().Be(EdgeOutboxStatus.Failed);
        rejected.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
    }

    [TestMethod]
    public async Task Forwarder_PersistsRetryWhenPipelineThrowsNonHttpResilienceException()
    {
        await using var fixture = await OutboxFixture.CreateAsync(
            handlerException: new InvalidOperationException("simulated open circuit"));
        await fixture.Writer.EnqueueAsync(Payload("eg4-a", "a", fixture.Now), CancellationToken.None);

        (await fixture.Forwarder.SweepAsync(CancellationToken.None)).Should().BeFalse();

        await using var db = fixture.CreateDb();
        var record = await db.OutboxRecords.SingleAsync();
        record.Status.Should().Be(EdgeOutboxStatus.Pending);
        record.AttemptCount.Should().Be(1);
        record.LastError.Should().Be("Transient forwarding pipeline failure");
    }

    [TestMethod]
    public async Task Forwarder_RequeuesRetryExhaustedRecordsWithoutNewTelemetry()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        await fixture.Writer.EnqueueAsync(Payload("eg4-a", "a", fixture.Now), CancellationToken.None);
        await using (var db = fixture.CreateDb())
        {
            var record = await db.OutboxRecords.SingleAsync();
            record.Status = EdgeOutboxStatus.Failed;
            record.FailureKind = EdgeOutboxFailureKind.RetryExhausted;
            record.AttemptCount = 3;
            await db.SaveChangesAsync();
        }

        await fixture.Forwarder.RequeueRetryExhaustedAsync(CancellationToken.None);

        await using var verification = fixture.CreateDb();
        var requeued = await verification.OutboxRecords.SingleAsync();
        requeued.Status.Should().Be(EdgeOutboxStatus.Pending);
        requeued.FailureKind.Should().Be(EdgeOutboxFailureKind.None);
        requeued.AttemptCount.Should().Be(0);
    }

    [TestMethod]
    public async Task Forwarder_CompactsExpiredSentAndFailedRecords()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        await fixture.Writer.EnqueueAsync(Payload("eg4-sent", "sent", fixture.Now), CancellationToken.None);
        await fixture.Writer.EnqueueAsync(Payload("eg4-failed", "failed", fixture.Now.AddSeconds(1)), CancellationToken.None);
        await using (var db = fixture.CreateDb())
        {
            var records = await db.OutboxRecords.OrderBy(record => record.SourceId).ToArrayAsync();
            var failed = records.Single(record => record.SourceId == "eg4-failed");
            failed.Status = EdgeOutboxStatus.Failed;
            failed.FailureKind = EdgeOutboxFailureKind.Permanent;
            failed.LastAttemptedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var sent = records.Single(record => record.SourceId == "eg4-sent");
            sent.Status = EdgeOutboxStatus.Sent;
            sent.SentAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        await fixture.Forwarder.CompactAsync(CancellationToken.None);

        await using var verification = fixture.CreateDb();
        (await verification.OutboxRecords.CountAsync()).Should().Be(0);
    }

    private static PowerReadingPayload Payload(string source, string device, DateTime time) => new()
    {
        SourceId = source,
        SourceSystem = "eg4-6500ex",
        DeviceId = device,
        RecordedAtUtc = time,
        BatteryVoltageV = 54.4,
        BatteryCurrentA = -10,
        BatteryPowerW = -544,
    };

    private sealed class OutboxFixture : IAsyncDisposable
    {
        private readonly string _path;
        private readonly ServiceProvider _services;
        private readonly IServiceScope _writerScope;
        private readonly GatewayTelemetry _telemetry;
        public DateTime Now { get; } = new(2026, 8, 9, 19, 0, 0, DateTimeKind.Utc);
        public PowerOutboxWriter Writer { get; }
        public PowerApiForwarder Forwarder { get; }
        public StubHandler Handler { get; }

        private OutboxFixture(string path, ServiceProvider services, IServiceScope writerScope, GatewayTelemetry telemetry, PowerOutboxWriter writer, PowerApiForwarder forwarder, StubHandler handler)
        {
            _path = path;
            _services = services;
            _writerScope = writerScope;
            _telemetry = telemetry;
            Writer = writer;
            Forwarder = forwarder;
            Handler = handler;
        }

        public static async Task<OutboxFixture> CreateAsync(
            HttpStatusCode status = HttpStatusCode.Created,
            string responseBody = "{\"inserted\":1,\"skipped\":0,\"failed\":[]}",
            Exception? handlerException = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"eg4-outbox-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OutboxDbContext>(options => options.UseSqlite($"Data Source={path}"));
            services.AddScoped<EdgeOutboxStore<OutboxDbContext>>();
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                    scope.ServiceProvider.GetRequiredService<OutboxDbContext>(),
                    Eg4OutboxPayloadTypes.Reading,
                    Eg4OutboxPayloadTypes.ReadingVersion);
            }
            var writerScope = provider.CreateScope();
            var writer = new PowerOutboxWriter(
                writerScope.ServiceProvider.GetRequiredService<EdgeOutboxStore<OutboxDbContext>>(),
                NullLogger<PowerOutboxWriter>.Instance);
            var handler = new StubHandler(status, responseBody, handlerException);
            var telemetry = new GatewayTelemetry(new("eg4-test", "battery-gateway"));
            var options = Options.Create(new OutboxOptions
            {
                ApiEndpoint = "https://example.invalid/api/v1/power/readings",
                ApiKey = "test-key",
                MaxRetryAttempts = 3,
                MaxBackoffSeconds = 30,
            });
            var forwarder = new PowerApiForwarder(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new StubHttpClientFactory(handler),
                options,
                new RuntimeOutboxSettings(),
                telemetry,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 9, 19, 0, 0, TimeSpan.Zero)),
                NullLogger<PowerApiForwarder>.Instance);
            return new OutboxFixture(path, provider, writerScope, telemetry, writer, forwarder, handler);
        }

        public OutboxDbContext CreateDb() => new(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite($"Data Source={_path}").Options);

        public async ValueTask DisposeAsync()
        {
            Forwarder.Dispose();
            _writerScope.Dispose();
            _telemetry.Dispose();
            await _services.DisposeAsync();
            try { File.Delete(_path); } catch (IOException) { }
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string responseBody, Exception? exception) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (exception is not null) throw exception;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

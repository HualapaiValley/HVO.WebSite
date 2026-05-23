using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Tests.Outbox;

[TestClass]
public sealed class PowerApiForwarderTests
{
    private SqliteConnection _conn = null!;
    private ServiceProvider _provider = null!;
    private CapturingHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"inserted\":1,\"skipped\":0,\"failed\":[]}", Encoding.UTF8, "application/json")
        });

        var services = new ServiceCollection();
        services.AddDbContext<OutboxDbContext>(o => o.UseSqlite(_conn));
        services.AddSingleton<IOptions<OutboxOptions>>(Options.Create(new OutboxOptions
        {
            ApiEndpoint = "https://hvo.example/api/v1/power/readings",
            ApiKey = "test-key",
            BatchSize = 10,
            MaxRetryAttempts = 2,
            MaxBackoffSeconds = 30,
            SweepIntervalSeconds = 60,
        }));
        services.AddSingleton<IHttpClientFactory>(_ => new TestHttpClientFactory(_handler, "test-key"));
        services.AddSingleton<PowerApiForwarder>(sp => new PowerApiForwarder(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptions<OutboxOptions>>(),
            NullLogger<PowerApiForwarder>.Instance));

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
    public async Task SweepAsync_SuccessfulPost_MarksRecordsSent()
    {
        await SeedRecordAsync("solarassistant-total", "2026-05-23T11:00:00Z");

        await _provider.GetRequiredService<PowerApiForwarder>().SweepAsync(CancellationToken.None);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = db.OutboxRecords.Single();
        row.Status.Should().Be(OutboxStatus.Sent);
        row.SentAtUtc.Should().NotBeNull();
        _handler.Requests.Should().HaveCount(1);
        _handler.Requests[0].RequestUri!.ToString().Should().Be("https://hvo.example/api/v1/power/readings");
        _handler.Requests[0].Headers.GetValues("X-Api-Key").Single().Should().Be("test-key");
    }

    [TestMethod]
    public async Task SweepAsync_ValidationFailure_MarksRecordFailed()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"inserted\":0,\"skipped\":0,\"failed\":[{\"sourceId\":\"solarassistant-total\",\"recordedAtUtc\":\"2026-05-23T11:05:00Z\",\"error\":\"bad payload\"}]}", Encoding.UTF8, "application/json")
        };
        await SeedRecordAsync("solarassistant-total", "2026-05-23T11:05:00Z");

        await _provider.GetRequiredService<PowerApiForwarder>().SweepAsync(CancellationToken.None);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = db.OutboxRecords.Single();
        row.Status.Should().Be(OutboxStatus.Failed);
        row.LastError.Should().Be("bad payload");
    }

    [TestMethod]
    public async Task SweepAsync_HttpFailure_SchedulesRetry()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("temporary outage", Encoding.UTF8, "text/plain")
        };
        await SeedRecordAsync("solarassistant-total", "2026-05-23T11:10:00Z");

        await _provider.GetRequiredService<PowerApiForwarder>().SweepAsync(CancellationToken.None);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = db.OutboxRecords.Single();
        row.Status.Should().Be(OutboxStatus.Pending);
        row.AttemptCount.Should().Be(1);
        row.NextRetryAtUtc.Should().BeAfter(DateTime.UtcNow);
        row.LastError.Should().Contain("503");
    }

    [TestMethod]
    public async Task SweepAsync_InvalidJsonPayload_MarksRecordFailedAndDoesNotPost()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.OutboxRecords.Add(new OutboxRecord
            {
                SourceId = "solarassistant-total",
                DeviceId = "total",
                RecordedAtUtc = DateTime.Parse("2026-05-23T11:15:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                Payload = "not-json",
            });
            await db.SaveChangesAsync();
        }

        await _provider.GetRequiredService<PowerApiForwarder>().SweepAsync(CancellationToken.None);

        using var verifyScope = _provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = verifyDb.OutboxRecords.Single();
        row.Status.Should().Be(OutboxStatus.Failed);
        row.LastError.Should().Be("Outbox payload JSON is invalid.");
        _handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SweepAsync_NullJsonPayload_MarksRecordFailedAndDoesNotPost()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.OutboxRecords.Add(new OutboxRecord
            {
                SourceId = "solarassistant-total",
                DeviceId = "total",
                RecordedAtUtc = DateTime.Parse("2026-05-23T11:20:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                Payload = "null",
            });
            await db.SaveChangesAsync();
        }

        await _provider.GetRequiredService<PowerApiForwarder>().SweepAsync(CancellationToken.None);

        using var verifyScope = _provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = verifyDb.OutboxRecords.Single();
        row.Status.Should().Be(OutboxStatus.Failed);
        row.LastError.Should().Be("Outbox payload JSON is invalid.");
        _handler.Requests.Should().BeEmpty();
    }

    private async Task SeedRecordAsync(string sourceId, string recordedAt)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var payload = new PowerReadingPayload
        {
            SourceId = sourceId,
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
            PvPowerW = 1200,
        };

        db.OutboxRecords.Add(new OutboxRecord
        {
            SourceId = sourceId,
            DeviceId = "total",
            RecordedAtUtc = payload.RecordedAtUtc,
            Payload = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        });
        await db.SaveChangesAsync();
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly string _apiKey;

        public TestHttpClientFactory(HttpMessageHandler handler, string apiKey)
        {
            _handler = handler;
            _apiKey = apiKey;
        }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_handler, disposeHandler: false);
            client.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
            return client;
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }
        public List<HttpRequestMessage> Requests { get; } = [];

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            Responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(Responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return clone;
        }
    }
}

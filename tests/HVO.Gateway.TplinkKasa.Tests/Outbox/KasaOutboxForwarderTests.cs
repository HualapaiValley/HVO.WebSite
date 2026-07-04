using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Outbox;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Models;
using HVO.Gateway.TplinkKasa.Outbox;
using HVO.Gateway.TplinkKasa.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Outbox;

[TestClass]
public sealed class KasaOutboxForwarderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task SweepAsync_ForwardsEnergyAndInventory_InSameSweep()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        await fixture.Store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: "tplink-kasa:desk-lamp",
            RecordedAtUtc: new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc),
            PayloadType: KasaOutboxPayloadTypes.Energy,
            PayloadVersion: KasaOutboxPayloadTypes.EnergyVersion,
            PayloadJson: JsonSerializer.Serialize(new KasaEnergyPayload
            {
                SourceId = "tplink-kasa:desk-lamp",
                DeviceId = "device-1",
                RecordedAtUtc = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc),
                LoadPowerW = 12.3,
                GridVoltageV = 119.8,
            }, JsonOptions),
            DeviceId: "device-1"), CancellationToken.None);
        await fixture.Store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: "tplink-kasa:desk-lamp",
            RecordedAtUtc: new DateTime(2026, 6, 16, 12, 0, 1, DateTimeKind.Utc),
            PayloadType: KasaOutboxPayloadTypes.Inventory,
            PayloadVersion: KasaOutboxPayloadTypes.InventoryVersion,
            PayloadJson: JsonSerializer.Serialize(new KasaInventoryPayload
            {
                SourceId = "tplink-kasa:desk-lamp",
                DeviceId = "device-1",
                RecordedAtUtc = new DateTime(2026, 6, 16, 12, 0, 1, DateTimeKind.Utc),
                Alias = "Desk Lamp",
                Model = "KP115(US)",
                SoftwareVersion = "1.2.3",
                Capabilities = ["EnergyRealtime"],
            }, JsonOptions),
            DeviceId: "device-1"), CancellationToken.None);

        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"failed\":[]}", Encoding.UTF8, "application/json")
        });
        var forwarder = CreateForwarder(fixture, handler);

        var anySent = await InvokeSweepAsync(forwarder);

        anySent.Should().BeTrue();
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal(
            "/api/v1/power/readings",
            "/api/v1/power/device-inventory");
        handler.Requests[0].Body.Should().StartWith("[");
        handler.Requests[0].Body.Should().Contain("loadPowerW");
        handler.Requests[0].Body.Should().Contain("gridVoltageV");
        handler.Requests[1].Body.Should().StartWith("{");
        handler.Requests[1].Body.Should().Contain("devices");
        fixture.Context.ChangeTracker.Clear();
        (await fixture.Context.OutboxRecords.CountAsync(r => r.Status == EdgeOutboxStatus.Sent)).Should().Be(2);
    }

    [TestMethod]
    public async Task SweepAsync_DeadLetters_ForForbiddenResponse()
    {
        await using var fixture = await OutboxFixture.CreateAsync();
        await fixture.Store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: "tplink-kasa:desk-lamp",
            RecordedAtUtc: new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc),
            PayloadType: KasaOutboxPayloadTypes.Energy,
            PayloadVersion: KasaOutboxPayloadTypes.EnergyVersion,
            PayloadJson: JsonSerializer.Serialize(new KasaEnergyPayload
            {
                SourceId = "tplink-kasa:desk-lamp",
                RecordedAtUtc = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc),
                LoadPowerW = 12.3,
            }, JsonOptions)), CancellationToken.None);

        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("forbidden", Encoding.UTF8, "text/plain")
        });
        var forwarder = CreateForwarder(fixture, handler);

        var anySent = await InvokeSweepAsync(forwarder);

        anySent.Should().BeFalse();
        fixture.Context.ChangeTracker.Clear();
        var record = await fixture.Context.OutboxRecords.SingleAsync();
        record.Status.Should().Be(EdgeOutboxStatus.Failed);
        record.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
        record.AttemptCount.Should().Be(1);
        record.LastError.Should().Contain("HTTP 403");
    }

    private static KasaOutboxForwarder CreateForwarder(OutboxFixture fixture, RecordingHandler handler)
    {
        var httpClientFactory = new RecordingHttpClientFactory(handler);
        return new KasaOutboxForwarder(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            Options.Create(new KasaGatewayOptions.OutboxSection
            {
                ApiEndpoint = "https://example.test/api/v1/power/readings",
                ApiKey = "secret",
                BatchSize = 50,
                MaxRetryAttempts = 10,
                MaxBackoffSeconds = 300,
                SentRetentionDays = 7,
                FailedRetentionDays = 30,
                SweepIntervalSeconds = 5,
            }),
            new RuntimeOutboxSettings(),
            NullLogger<KasaOutboxForwarder>.Instance);
    }

    private static async Task<bool> InvokeSweepAsync(KasaOutboxForwarder forwarder)
    {
        var method = typeof(KasaOutboxForwarder).GetMethod("SweepAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        method.Should().NotBeNull();
        var task = (Task<bool>)method!.Invoke(forwarder, [CancellationToken.None])!;
        return await task;
    }

    private sealed class OutboxFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private OutboxFixture(SqliteConnection connection, ServiceProvider services, OutboxDbContext context)
        {
            _connection = connection;
            Services = services;
            Context = context;
            Store = new EdgeOutboxStore<OutboxDbContext>(context);
        }

        public ServiceProvider Services { get; }
        public OutboxDbContext Context { get; }
        public EdgeOutboxStore<OutboxDbContext> Store { get; }

        public static async Task<OutboxFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection()
                .AddDbContext<OutboxDbContext>(options => options.UseSqlite(connection))
                .AddScoped<EdgeOutboxStore<OutboxDbContext>>()
                .BuildServiceProvider();

            var context = services.GetRequiredService<OutboxDbContext>();
            await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(
                context,
                KasaOutboxPayloadTypes.Energy,
                KasaOutboxPayloadTypes.EnergyVersion);
            return new OutboxFixture(connection, services, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingHttpClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        public List<(Uri? RequestUri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri, body));
            return createResponse(request);
        }
    }
}

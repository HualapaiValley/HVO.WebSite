using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
public sealed class HomeAssistantObservationBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SameTimestampBurst_InEitherOrder_PersistsOnlyFinalIntendedPayload(bool voltageFirst)
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.Create());
        var timestamp = fixture.Clock.GetUtcNow();
        await fixture.Coordinator.ReconcileAsync([
            Power("100", timestamp.AddSeconds(-2)), Voltage("120", timestamp.AddSeconds(-2))], CancellationToken.None);
        var events = new[] { Power("200", timestamp), Voltage("121", timestamp) };
        foreach (var changed in voltageFirst ? events.Reverse() : events)
            await fixture.Coordinator.ApplyAsync(changed, CancellationToken.None);

        await fixture.Coordinator.FlushAsync(CancellationToken.None);
        (await fixture.RecordsAsync()).Should().BeEmpty("the receive-time coalescing window has not elapsed");
        await fixture.FlushAsync();

        var record = (await fixture.RecordsAsync()).Should().ContainSingle().Which;
        record.RecordedAtUtc.Should().Be(timestamp.UtcDateTime);
        Payload(record).GetProperty("loadPowerW").GetDouble().Should().Be(200);
        Payload(record).GetProperty("gridVoltageV").GetDouble().Should().Be(121);
        fixture.Projector.Project("kasa").Should().BeNull("the final signature was durably acknowledged");
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    public async Task CrossEntityOutOfOrderArrival_CoalescesWithoutChangingSourceTime()
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.Create());
        var timestamp = fixture.Clock.GetUtcNow();
        await fixture.Coordinator.ReconcileAsync([Power("100", timestamp), Voltage("120", timestamp)], CancellationToken.None);
        await fixture.Coordinator.ApplyAsync(Power("200", timestamp.AddSeconds(2)), CancellationToken.None);
        await fixture.Coordinator.ApplyAsync(Voltage("121", timestamp.AddSeconds(1)), CancellationToken.None);
        await fixture.Coordinator.ApplyAsync(Voltage("119", timestamp.AddSeconds(-1)), CancellationToken.None);
        await fixture.FlushAsync();

        var record = (await fixture.RecordsAsync()).Should().ContainSingle().Which;
        record.RecordedAtUtc.Should().Be(timestamp.AddSeconds(2).UtcDateTime);
        Payload(record).GetProperty("gridVoltageV").GetDouble().Should().Be(121);
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GoveeRequiredFields_InEitherOrder_ProduceOneCompleteObservation(bool humidityFirst)
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.Create());
        var timestamp = fixture.Clock.GetUtcNow();
        var events = new[] { Temperature("20", timestamp), Humidity("42", timestamp) };
        foreach (var changed in humidityFirst ? events.Reverse() : events)
            await fixture.Coordinator.ApplyAsync(changed, CancellationToken.None);
        await fixture.FlushAsync();

        var record = (await fixture.RecordsAsync()).Should().ContainSingle().Which;
        Payload(record).GetProperty("temperatureF").GetDouble().Should().Be(68);
        Payload(record).GetProperty("humidityPercent").GetDouble().Should().Be(42);
        fixture.Projector.Project("govee").Should().BeNull();
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    [DataRow("unavailable", 0)]
    [DataRow("40", -301)]
    [DataRow("40", -31)]
    [DataRow("40", 31)]
    public async Task RequiredUnavailableStaleSkewedOrFutureField_SuppressesThenRecovers(string humidity, int offset)
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.Create());
        var timestamp = fixture.Clock.GetUtcNow();
        await fixture.Coordinator.ReconcileAsync([
            Temperature("20", timestamp), Humidity(humidity, timestamp.AddSeconds(offset))], CancellationToken.None);
        await fixture.FlushAsync();
        (await fixture.RecordsAsync()).Should().BeEmpty();
        fixture.State.Snapshot().LastObservationUtc.Should().BeNull();

        // A corrected required state must not be older than the entity's known watermark.
        var recoveredAt = timestamp.AddSeconds(Math.Max(offset, 0));
        fixture.Clock.Advance(TimeSpan.FromSeconds(Math.Max(offset, 0)));
        await fixture.Coordinator.ApplyAsync(Temperature("20", recoveredAt), CancellationToken.None);
        await fixture.Coordinator.ApplyAsync(Humidity("40", recoveredAt), CancellationToken.None);
        await fixture.FlushAsync();
        (await fixture.RecordsAsync()).Should().ContainSingle();
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    public async Task RequiredFieldBecomesUnavailableDuringWindow_CancelsProjectionAndSameValueRecoveryPersists()
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.PowerOnly());
        var timestamp = fixture.Clock.GetUtcNow();
        await fixture.Coordinator.ApplyAsync(Power("100", timestamp), CancellationToken.None);
        await fixture.Coordinator.ApplyAsync(Power("unavailable", timestamp.AddSeconds(1)), CancellationToken.None);
        await fixture.FlushAsync();
        (await fixture.RecordsAsync()).Should().BeEmpty();
        await fixture.Coordinator.ApplyAsync(Power("100", timestamp.AddSeconds(2)), CancellationToken.None);
        await fixture.FlushAsync();
        (await fixture.RecordsAsync()).Should().ContainSingle();
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    public async Task RequiredFieldExpiresBeforeFlush_IsNotAcknowledged()
    {
        var options = TestOptions.PowerOnly();
        options.RequiredFieldFreshnessSeconds = 1;
        await using var fixture = await Fixture.CreateAsync(options);
        await fixture.Coordinator.ApplyAsync(Power("100", fixture.Clock.GetUtcNow()), CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Coordinator.FlushAsync(CancellationToken.None);
        (await fixture.RecordsAsync()).Should().BeEmpty();
        fixture.Writer.Attempts.Should().BeEmpty();
    }

    [TestMethod]
    public async Task OptionalArrivalRemovalAndStaleness_ArePartOfIntendedDurableContent()
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.Create());
        var timestamp = fixture.Clock.GetUtcNow();
        await fixture.Coordinator.ReconcileAsync([Power("100", timestamp), Voltage("120", timestamp.AddSeconds(-301))], CancellationToken.None);
        await fixture.FlushAsync();
        Payload((await fixture.RecordsAsync()).Single()).GetProperty("gridVoltageV").ValueKind.Should().Be(JsonValueKind.Null);

        await fixture.Coordinator.ApplyAsync(Voltage("121", timestamp.AddSeconds(1)), CancellationToken.None);
        await fixture.FlushAsync();
        await fixture.Coordinator.ApplyAsync(Power("100", timestamp.AddSeconds(2)), CancellationToken.None);
        await fixture.Coordinator.ApplyAsync(Voltage("unavailable", timestamp.AddSeconds(2)), CancellationToken.None);
        await fixture.FlushAsync();
        var records = await fixture.RecordsAsync();
        records.Should().HaveCount(3);
        Payload(records[1]).GetProperty("gridVoltageV").GetDouble().Should().Be(121);
        Payload(records[2]).GetProperty("gridVoltageV").ValueKind.Should().Be(JsonValueKind.Null);
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task SameIdentityConflict_RemainsUnacknowledgedAcrossReconciliationAndRestart_AndPreservesSentHistory(int lateVoltageOffset)
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.Create());
        var timestamp = fixture.Clock.GetUtcNow();
        await fixture.Coordinator.ReconcileAsync([Power("100", timestamp), Voltage("120", timestamp.AddSeconds(-2))], CancellationToken.None);
        await fixture.FlushAsync();
        await fixture.MarkSentAsync();
        var original = (await fixture.RecordsAsync()).Single().PayloadJson;

        await fixture.Coordinator.ApplyAsync(Voltage("121", timestamp.AddSeconds(lateVoltageOffset)), CancellationToken.None);
        await fixture.FlushAsync();
        fixture.Writer.Attempts.Last().Outcome.Should().Be(HomeAssistantPersistenceOutcome.Conflict);
        fixture.Projector.Project("kasa").Should().NotBeNull();
        fixture.State.Snapshot().ConflictingMappings.Should().ContainSingle().Which.Should().Be("kasa");
        fixture.State.SetConnected();
        fixture.State.Snapshot().ConflictingMappings.Should().Contain("kasa", "a reconnect must not clear durability alerts");
        await fixture.FlushAsync();
        fixture.Writer.Attempts.Last().Outcome.Should().Be(HomeAssistantPersistenceOutcome.Conflict, "conflicts stay eligible for retry");

        fixture.Restart();
        await fixture.Coordinator.ReconcileAsync([Power("100", timestamp), Voltage("121", timestamp.AddSeconds(lateVoltageOffset))], CancellationToken.None);
        await fixture.FlushAsync();
        fixture.Projector.Project("kasa").Should().NotBeNull();
        fixture.State.Snapshot().ConflictingMappings.Should().Contain("kasa");
        var record = (await fixture.RecordsAsync()).Should().ContainSingle().Which;
        record.Status.Should().Be(EdgeOutboxStatus.Sent);
        record.PayloadJson.Should().Be(original);

        await fixture.Coordinator.ApplyAsync(Power("100", timestamp.AddSeconds(1)), CancellationToken.None);
        await fixture.FlushAsync();
        fixture.State.Snapshot().ConflictingMappings.Should().BeEmpty("a later source watermark can persist the current content");
        (await fixture.RecordsAsync()).Should().HaveCount(2);
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    public async Task RestartIdenticalReplay_ProvesEqualityBeforeAcknowledgement()
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.PowerOnly());
        var snapshot = new[] { Power("100", fixture.Clock.GetUtcNow()) };
        await fixture.Coordinator.ReconcileAsync(snapshot, CancellationToken.None);
        await fixture.FlushAsync();
        fixture.Restart();
        await fixture.Coordinator.ReconcileAsync(snapshot, CancellationToken.None);
        await fixture.FlushAsync();

        fixture.Writer.Attempts.Last().Outcome.Should().Be(HomeAssistantPersistenceOutcome.IdenticalReplay);
        fixture.Projector.Project("kasa").Should().BeNull();
        (await fixture.RecordsAsync()).Should().ContainSingle();
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    public async Task CancelledFlush_DoesNotAcknowledgeAndCanBeRetried()
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.PowerOnly());
        await fixture.Coordinator.ApplyAsync(Power("100", fixture.Clock.GetUtcNow()), CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await fixture.Coordinator.Invoking(value => value.FlushAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        fixture.Projector.Project("kasa").Should().NotBeNull();
        (await fixture.RecordsAsync()).Should().BeEmpty();
        await fixture.FlushAsync();
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    public async Task FailedWrite_RemainsUnacknowledgedUntilDurableRetry()
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.PowerOnly());
        await fixture.Coordinator.ApplyAsync(Power("100", fixture.Clock.GetUtcNow()), CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Writer.FailNext = true;
        await fixture.Coordinator.Invoking(value => value.FlushAsync(CancellationToken.None)).Should().ThrowAsync<IOException>();
        fixture.Projector.Project("kasa").Should().NotBeNull();
        fixture.State.Snapshot().LastObservationUtc.Should().BeNull();
        (await fixture.RecordsAsync()).Should().BeEmpty();
        await fixture.FlushAsync();
        fixture.Projector.Project("kasa").Should().BeNull();
        await fixture.AssertAcknowledgedDataAsync();
    }

    [TestMethod]
    public async Task InFlightWrite_SerializesChangesAndFlushes_WithoutAcknowledgingNewerState()
    {
        await using var fixture = await Fixture.CreateAsync(TestOptions.PowerOnly());
        var timestamp = fixture.Clock.GetUtcNow();
        await fixture.Coordinator.ApplyAsync(Power("100", timestamp), CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Writer.BlockNext = true;
        var flushing = fixture.Coordinator.FlushAsync(CancellationToken.None);
        await fixture.Writer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var applying = fixture.Coordinator.ApplyAsync(Power("200", timestamp.AddSeconds(1)), CancellationToken.None);
        applying.IsCompleted.Should().BeFalse("projection changes cannot race durable acknowledgement");
        fixture.Writer.Release.TrySetResult();
        await Task.WhenAll(flushing, applying);
        fixture.Projector.Project("kasa").Should().NotBeNull("the newer state has not been acknowledged by the earlier write");
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(fixture.Coordinator.FlushAsync(CancellationToken.None), fixture.Coordinator.FlushAsync(CancellationToken.None));
        fixture.Writer.MaxActive.Should().Be(1);
        (await fixture.RecordsAsync()).Should().HaveCount(2);
        fixture.Projector.Project("kasa").Should().BeNull();
        await fixture.AssertAcknowledgedDataAsync();
    }

    private static JsonElement Payload(EdgeOutboxRecord record) =>
        JsonSerializer.Deserialize<HomeAssistantOutboxPayload>(record.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!.Payload;
    private static HomeAssistantState Power(string value, DateTimeOffset time) => State("sensor.kasa_power", value, "W", "power", time);
    private static HomeAssistantState Voltage(string value, DateTimeOffset time) => State("sensor.kasa_voltage", value, "V", "voltage", time);
    private static HomeAssistantState Temperature(string value, DateTimeOffset time) => State("sensor.govee_temperature", value, "°C", "temperature", time);
    private static HomeAssistantState Humidity(string value, DateTimeOffset time) => State("sensor.govee_humidity", value, "%", "humidity", time);
    private static HomeAssistantState State(string id, string value, string unit, string deviceClass, DateTimeOffset time) =>
        new(id, value, JsonSerializer.SerializeToElement(new { unit_of_measurement = unit, device_class = deviceClass }), time);

    private sealed class RecordingWriter(IHomeAssistantObservationWriter inner) : IHomeAssistantObservationWriter
    {
        private int active;
        public bool FailNext { get; set; }
        public bool BlockNext { get; set; }
        public int MaxActive { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(HomeAssistantMappedObservation Observation, HomeAssistantPersistenceOutcome Outcome)> Attempts { get; } = [];
        public async Task<HomeAssistantPersistenceOutcome> EnqueueAsync(HomeAssistantMappedObservation observation, CancellationToken cancellationToken)
        {
            active++;
            MaxActive = Math.Max(MaxActive, active);
            try
            {
                if (FailNext)
                {
                    FailNext = false;
                    throw new IOException("Controlled durable-write failure.");
                }
                if (BlockNext)
                {
                    BlockNext = false;
                    Started.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                }
                var outcome = await inner.EnqueueAsync(observation, cancellationToken);
                Attempts.Add((observation, outcome));
                return outcome;
            }
            finally { active--; }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hvo-ha-boundary", Guid.NewGuid().ToString("N"));
        private ServiceProvider services = null!;
        private readonly HomeAssistantExporterOptions options;
        public TestTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-08-11T10:00:00Z"));
        public HomeAssistantStateProjector Projector { get; private set; } = null!;
        public HomeAssistantExporterState State { get; private set; } = null!;
        public HomeAssistantObservationCoordinator Coordinator { get; private set; } = null!;
        public RecordingWriter Writer { get; private set; } = null!;
        private Fixture(HomeAssistantExporterOptions options) => this.options = options;

        public static async Task<Fixture> CreateAsync(HomeAssistantExporterOptions options)
        {
            var fixture = new Fixture(options);
            Directory.CreateDirectory(fixture.directory);
            var collection = new ServiceCollection();
            collection.AddDbContext<DefaultEdgeOutboxDbContext>(builder => builder.UseSqlite($"Data Source={Path.Combine(fixture.directory, "outbox.db")};Pooling=False"));
            collection.AddScoped<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
            fixture.services = collection.BuildServiceProvider();
            await using var scope = fixture.services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>().Database.EnsureCreatedAsync();
            fixture.Restart();
            return fixture;
        }

        public void Restart()
        {
            Coordinator?.Dispose();
            Projector = new HomeAssistantStateProjector(Options.Create(options), Clock);
            State = new HomeAssistantExporterState();
            Writer = new RecordingWriter(new HomeAssistantObservationWriter(services.GetRequiredService<IServiceScopeFactory>()));
            Coordinator = new HomeAssistantObservationCoordinator(Projector, Writer, State, Options.Create(options), Clock,
                NullLogger<HomeAssistantObservationCoordinator>.Instance);
        }

        public async Task FlushAsync()
        {
            Clock.Advance(TimeSpan.FromMilliseconds(options.CoalescingWindowMilliseconds));
            await Coordinator.FlushAsync(CancellationToken.None);
        }

        public async Task<List<EdgeOutboxRecord>> RecordsAsync()
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>().OutboxRecords
                .AsNoTracking().OrderBy(record => record.RecordedAtUtc).ToListAsync();
        }

        public async Task MarkSentAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
            store.MarkSent(await store.Db.OutboxRecords.SingleAsync(), Clock.GetUtcNow().UtcDateTime);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        public async Task AssertAcknowledgedDataAsync()
        {
            var records = await RecordsAsync();
            Writer.Attempts.Where(attempt => attempt.Outcome != HomeAssistantPersistenceOutcome.Conflict).Should().NotBeEmpty();
            foreach (var attempt in Writer.Attempts.Where(attempt => attempt.Outcome != HomeAssistantPersistenceOutcome.Conflict))
            {
                var row = records.Single(record => record.SourceId == attempt.Observation.SourceId
                    && record.RecordedAtUtc == attempt.Observation.RecordedAtUtc.UtcDateTime);
                row.PayloadJson.Should().Be(HomeAssistantObservationSerialization.Serialize(attempt.Observation.Contract, attempt.Observation.Payload));
            }
        }

        public async ValueTask DisposeAsync()
        {
            Coordinator.Dispose();
            await services.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}

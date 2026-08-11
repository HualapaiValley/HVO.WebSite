using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HVO.Edge.Outbox.Tests;

[TestClass]
public sealed class EdgeOutboxForwarderTests
{
    [TestMethod]
    public async Task Forwarder_MarksSuccessfulRecordSent()
    {
        await RunOutcomeTestAsync(
            record => new EdgeOutboxSendOutcome(record.Id, EdgeOutboxSendStatus.Sent),
            record => record.Status == EdgeOutboxStatus.Sent,
            record =>
            {
                record.Status.Should().Be(EdgeOutboxStatus.Sent);
                record.SentAtUtc.Should().NotBeNull();
                record.AttemptCount.Should().Be(1);
            });
    }

    [TestMethod]
    public async Task Forwarder_SchedulesTransientFailureForRetry()
    {
        await RunOutcomeTestAsync(
            record => new EdgeOutboxSendOutcome(record.Id, EdgeOutboxSendStatus.TransientFailure, "service unavailable"),
            record => record.NextRetryAtUtc > DateTime.MinValue,
            record =>
            {
                record.Status.Should().Be(EdgeOutboxStatus.Pending);
                record.NextRetryAtUtc.Should().BeAfter(record.LastAttemptedAtUtc!.Value);
                record.LastError.Should().Be("service unavailable");
                record.FailureKind.Should().Be(EdgeOutboxFailureKind.None);
            });
    }

    [TestMethod]
    public async Task Forwarder_DeadLettersPermanentFailure()
    {
        await RunOutcomeTestAsync(
            record => new EdgeOutboxSendOutcome(record.Id, EdgeOutboxSendStatus.PermanentFailure, "invalid payload"),
            record => record.Status == EdgeOutboxStatus.Failed,
            record =>
            {
                record.Status.Should().Be(EdgeOutboxStatus.Failed);
                record.FailureKind.Should().Be(EdgeOutboxFailureKind.Permanent);
                record.LastError.Should().Be("invalid payload");
            });
    }

    [TestMethod]
    public async Task Forwarder_RedactsAndBoundsPersistedSenderErrors()
    {
        var secret = "super-secret-value";
        await RunOutcomeTestAsync(
            record => new EdgeOutboxSendOutcome(
                record.Id,
                EdgeOutboxSendStatus.PermanentFailure,
                $"Authorization: Bearer {secret} {new string('x', 2_000)}"),
            record => record.LastError is not null,
            record =>
            {
                record.LastError.Should().NotContain(secret);
                record.LastError.Should().Contain("[REDACTED]");
                record.LastError.Should().HaveLength(1024);
            });
    }

    [TestMethod]
    public async Task SenderException_DoesNotTerminateForwarderAndSchedulesRetry()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new DelegateSender((_, _) =>
        {
            invoked.TrySetResult();
            throw new HttpRequestException("offline");
        });

        await using var fixture = await ForwarderFixture.CreateAsync(sender);
        await fixture.Forwarder.StartAsync(CancellationToken.None);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var record = await fixture.WaitForRecordAsync(row => row.LastError is not null);

        record.Status.Should().Be(EdgeOutboxStatus.Pending);
        record.LastError.Should().Be("Outbox batch sender failed");
        fixture.Forwarder.ExecuteTask.Should().NotBeNull();
        fixture.Forwarder.ExecuteTask!.IsCompleted.Should().BeFalse();

        await fixture.Forwarder.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task StopAsync_CancelsSenderAndStopsPromptly()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new DelegateSender(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                throw;
            }

            return [];
        });

        await using var fixture = await ForwarderFixture.CreateAsync(sender);
        await fixture.Forwarder.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await fixture.Forwarder.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Forwarder.ExecuteTask!.IsCompleted.Should().BeTrue();
    }

    private static async Task RunOutcomeTestAsync(
        Func<EdgeOutboxRecord, EdgeOutboxSendOutcome> outcome,
        Func<EdgeOutboxRecord, bool> completed,
        Action<EdgeOutboxRecord> assertion)
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new DelegateSender((records, _) =>
        {
            sent.TrySetResult();
            return Task.FromResult<IReadOnlyList<EdgeOutboxSendOutcome>>(records.Select(outcome).ToArray());
        });

        await using var fixture = await ForwarderFixture.CreateAsync(sender);
        await fixture.Forwarder.StartAsync(CancellationToken.None);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var record = await fixture.WaitForRecordAsync(completed);

        assertion(record);
        await fixture.Forwarder.StopAsync(CancellationToken.None);
    }

    private sealed class DelegateSender(
        Func<IReadOnlyList<EdgeOutboxRecord>, CancellationToken, Task<IReadOnlyList<EdgeOutboxSendOutcome>>> send)
        : IEdgeOutboxBatchSender
    {
        public Task<IReadOnlyList<EdgeOutboxSendOutcome>> SendAsync(
            IReadOnlyList<EdgeOutboxRecord> records,
            CancellationToken cancellationToken) => send(records, cancellationToken);
    }

    private sealed class ForwarderFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _root;

        private ForwarderFixture(ServiceProvider provider, string root, EdgeOutboxForwarder forwarder)
        {
            _provider = provider;
            _root = root;
            Forwarder = forwarder;
        }

        public EdgeOutboxForwarder Forwarder { get; }

        public static async Task<ForwarderFixture> CreateAsync(IEdgeOutboxBatchSender sender)
        {
            var root = Path.Combine(Path.GetTempPath(), $"hvo-edge-forwarder-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:DatabasePath"] = Path.Combine(root, "outbox.db"),
                ["Outbox:PayloadType"] = "test.reading",
                ["Outbox:PayloadVersion"] = "1",
                ["Outbox:SweepIntervalSeconds"] = "1",
                ["Outbox:SentRetentionDays"] = "0",
                ["Outbox:FailedRetentionDays"] = "0",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(sender);
            services.AddHvoEdgeOutbox(configuration);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            var hosted = provider.GetServices<IHostedService>().ToList();
            await hosted.OfType<EdgeOutboxInitializer>().Single().StartingAsync(CancellationToken.None);
            await using (var scope = provider.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
                await store.EnqueueAsync(new EdgeOutboxMessage(
                    "source-1",
                    DateTime.UtcNow.AddMinutes(-1),
                    "test.reading",
                    "1",
                    "{\"value\":42}"), CancellationToken.None);
            }

            return new ForwarderFixture(provider, root, hosted.OfType<EdgeOutboxForwarder>().Single());
        }

        public async Task<EdgeOutboxRecord> WaitForRecordAsync(Func<EdgeOutboxRecord, bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                await using var scope = _provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DefaultEdgeOutboxDbContext>();
                var record = await db.OutboxRecords.AsNoTracking().SingleAsync();
                if (predicate(record))
                    return record;
                await Task.Delay(20);
            }

            throw new TimeoutException("The outbox record did not reach the expected state.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Forwarder.ExecuteTask is { IsCompleted: false })
                await Forwarder.StopAsync(CancellationToken.None);
            await _provider.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }
}

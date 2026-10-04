using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Protocol.Packets;
using HVO.Hardware.DavisVantagePro2.WeatherUnderground;
using HVO.Hardware.DavisVantagePro2.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HVO.Hardware.DavisVantagePro2.Tests.WeatherUnderground;

[TestClass]
public sealed class WeatherUndergroundPublisherTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ExecuteAsync_Enabled_PublishesOnFiveSecondCadence()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var state = CurrentState(clock, 70);
        var calls = Channel.CreateUnbounded<DateTimeOffset>();
        var completed = Channel.CreateUnbounded<DateTimeOffset>();
        var logger = new RecordingLogger<WeatherUndergroundPublisher>(
            () => completed.Writer.TryWrite(clock.GetUtcNow()));
        await using var fixture = CreateFixture(
            state,
            clock,
            async (_, _) =>
            {
                calls.Writer.TryWrite(clock.GetUtcNow());
                // Exercise an asynchronous response: handler entry is not publication completion.
                await Task.Yield();
                return await SuccessResponse();
            },
            logger: logger);

        await fixture.Publisher.StartAsync(CancellationToken.None);
        var first = await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        first.Should().Be(InitialTime);
        // The success log follows response parsing, timeout disposal and state updates.
        // The periodic timer already exists before the first request, so it retains a tick
        // even if the service has not yet called WaitForNextTickAsync.
        (await completed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1))).Should().Be(InitialTime);
        for (var tick = 1; tick <= 3; tick++)
        {
            state.Observed(Observation(clock, 72), clock.GetUtcNow().UtcDateTime);
            clock.Advance(TimeSpan.FromSeconds(4));
            calls.Reader.TryRead(out _).Should().BeFalse("the five-second interval has not elapsed");
            clock.Advance(TimeSpan.FromSeconds(1));
            var next = await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            next.Should().Be(InitialTime.AddSeconds(5 * tick));
            (await completed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1))).Should().Be(next);
        }
        await fixture.Publisher.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ExecuteAsync_Disabled_PerformsNoRequests()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var attempts = 0;
        await using var fixture = CreateFixture(
            CurrentState(clock, 70),
            clock,
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return SuccessResponse();
            },
            enabled: false);

        await fixture.Publisher.StartAsync(CancellationToken.None);
        attempts.Should().Be(0);
        fixture.Publisher.ExecuteTask.Should().NotBeNull();
        await fixture.Publisher.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task PublishLatestAsync_RetryUsesLatestReadingInsteadOfReplayingOldReading()
    {
        var clock = new RetryTimeProvider(InitialTime);
        var state = CurrentState(clock, 70);
        var requests = new ConcurrentQueue<string>();
        var attempt = 0;
        await using var fixture = CreateFixture(
            state,
            clock,
            (request, _) =>
            {
                requests.Enqueue(request.RequestUri!.Query);
                if (Interlocked.Increment(ref attempt) == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                return SuccessResponse();
            });

        var publish = fixture.Publisher.PublishLatestAsync(CancellationToken.None);
        // A rejected HTTP handler has not necessarily registered the retry delay yet.
        await clock.RetryRegistered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        state.Observed(Observation(clock, 80), clock.GetUtcNow().UtcDateTime);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await publish;

        requests.Should().HaveCount(2);
        requests.ElementAt(0).Should().Contain("tempf=70");
        requests.ElementAt(1).Should().Contain("tempf=80");
        var snapshot = fixture.State.Snapshot();
        snapshot.LastSuccessAtUtc.Should().Be(clock.GetUtcNow().UtcDateTime);
        snapshot.ConsecutiveFailures.Should().Be(0);
        snapshot.LastError.Should().BeNull();
    }

    [TestMethod]
    public async Task PublishLatestAsync_RejectedResponseIsIsolatedAndLogsNoCredentialOrUrl()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var logger = new RecordingLogger<WeatherUndergroundPublisher>();
        await using var fixture = CreateFixture(
            CurrentState(clock, 70),
            clock,
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("invalid credentials"),
            }),
            logger: logger);

        await fixture.Publisher.PublishLatestAsync(CancellationToken.None);

        var snapshot = fixture.State.Snapshot();
        snapshot.ConsecutiveFailures.Should().Be(1);
        snapshot.LastError.Should().Be("response-rejected");
        logger.Messages.Should().ContainSingle(message => message.Contains("response-rejected", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains("synthetic-station-key", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains("wunderground.com", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PublishLatestAsync_StaleReadingIsSkippedWithoutHttpRequest()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var state = new DavisRuntimeState();
        state.Observed(
            Observation(clock, 70),
            clock.GetUtcNow().UtcDateTime.Subtract(TimeSpan.FromSeconds(11)));
        var attempts = 0;
        await using var fixture = CreateFixture(
            state,
            clock,
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return SuccessResponse();
            });

        await fixture.Publisher.PublishLatestAsync(CancellationToken.None);

        attempts.Should().Be(0);
        var snapshot = fixture.State.Snapshot();
        snapshot.LastError.Should().Be("stale-observation");
        snapshot.ConsecutiveFailures.Should().Be(0);
    }

    [TestMethod]
    public async Task PublishLatestAsync_SkippedReadingClearsPriorDeliveryFailures()
    {
        var clock = new FakeTimeProvider(InitialTime);
        var state = CurrentState(clock, 70);
        await using var fixture = CreateFixture(
            state,
            clock,
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("rejected"),
            }));

        await fixture.Publisher.PublishLatestAsync(CancellationToken.None);
        fixture.State.Snapshot().ConsecutiveFailures.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(11));
        await fixture.Publisher.PublishLatestAsync(CancellationToken.None);

        var snapshot = fixture.State.Snapshot();
        snapshot.ConsecutiveFailures.Should().Be(0);
        snapshot.LastError.Should().Be("stale-observation");
    }

    private static DavisRuntimeState CurrentState(FakeTimeProvider clock, double temperature)
    {
        var state = new DavisRuntimeState();
        state.Observed(Observation(clock, temperature), clock.GetUtcNow().UtcDateTime);
        return state;
    }

    private static Loop2Packet Observation(FakeTimeProvider clock, double temperature) => new()
    {
        RecordedAtUtc = clock.GetUtcNow().UtcDateTime,
        OutsideTemperatureF = temperature,
    };

    private static PublisherFixture CreateFixture(
        DavisRuntimeState state,
        FakeTimeProvider clock,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        bool enabled = true,
        ILogger<WeatherUndergroundPublisher>? logger = null)
    {
        var options = Options.Create(new WeatherUndergroundOptions
        {
            Enabled = enabled,
            StationId = "KAZKINGM12",
            IntervalSeconds = 5,
            RequestTimeoutSeconds = 2,
        });
        var httpClient = new HttpClient(new StubHandler(handler))
        {
            BaseAddress = new Uri("https://example.test/weatherstation/updateweatherstation.php"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var credential = new WeatherUndergroundCredential();
        credential.Initialize("synthetic-station-key");
        var publisherState = new WeatherUndergroundPublisherState();
        var metrics = new WeatherUndergroundMetrics();
        var publisher = new WeatherUndergroundPublisher(
            state,
            publisherState,
            new WeatherUndergroundClient(httpClient, options, clock),
            credential,
            metrics,
            options,
            clock,
            logger ?? new RecordingLogger<WeatherUndergroundPublisher>());
        return new(publisher, publisherState, httpClient, metrics);
    }

    private static Task<HttpResponseMessage> SuccessResponse() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("success"),
    });

    private sealed class PublisherFixture(
        WeatherUndergroundPublisher publisher,
        WeatherUndergroundPublisherState state,
        HttpClient httpClient,
        WeatherUndergroundMetrics metrics) : IAsyncDisposable
    {
        public WeatherUndergroundPublisher Publisher { get; } = publisher;
        public WeatherUndergroundPublisherState State { get; } = state;

        public async ValueTask DisposeAsync()
        {
            if (Publisher.ExecuteTask is { IsCompleted: false })
                await Publisher.StopAsync(CancellationToken.None);
            Publisher.Dispose();
            httpClient.Dispose();
            metrics.Dispose();
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class RetryTimeProvider(DateTimeOffset initialTime) : FakeTimeProvider(initialTime)
    {
        public TaskCompletionSource RetryRegistered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime == WeatherUndergroundOptions.RetryBackoff && period == Timeout.InfiniteTimeSpan)
                RetryRegistered.TrySetResult();
            return timer;
        }
    }

    private sealed class RecordingLogger<T>(Action? onCompleted = null) : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (logLevel == LogLevel.Debug)
                onCompleted?.Invoke();
        }
    }
}

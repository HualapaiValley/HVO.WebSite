using FluentAssertions;
using HVO.Edge.Hosting.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace HVO.Edge.Hosting.Tests.Logging;

[TestClass]
public sealed class RepeatedFailureFilterTests
{
    [TestMethod]
    public void IsEnabled_SuppressesRepeatedFailureUntilIntervalElapses()
    {
        var timeProvider = new FakeTimeProvider();
        var filter = new RepeatedFailureFilter(TimeSpan.FromMinutes(1), timeProvider);
        var first = CreateEvent(LogEventLevel.Warning, "Device poll failed");
        var repeated = CreateEvent(LogEventLevel.Warning, "Device poll failed");

        filter.IsEnabled(first).Should().BeTrue();
        filter.IsEnabled(repeated).Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        filter.IsEnabled(repeated).Should().BeTrue();
    }

    [TestMethod]
    public void IsEnabled_AllowsRecoveryAndNonFailureEvents()
    {
        var filter = new RepeatedFailureFilter(TimeSpan.FromMinutes(1));

        var failure = CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed", "device-1");
        filter.IsEnabled(failure).Should().BeTrue();
        filter.IsEnabled(CreateEvent(LogEventLevel.Information, "Device {SourceId} poll recovered", "device-1")).Should().BeTrue();
        filter.IsEnabled(failure).Should().BeTrue();
    }

    [TestMethod]
    public void IsEnabled_DoesNotConflateDifferentDevices()
    {
        var filter = new RepeatedFailureFilter(TimeSpan.FromMinutes(1));

        filter.IsEnabled(CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed", "device-1")).Should().BeTrue();
        filter.IsEnabled(CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed", "device-2")).Should().BeTrue();
    }

    [TestMethod]
    public void IsEnabled_RecoveryClearsAllMatchingFailureEntries()
    {
        var filter = new RepeatedFailureFilter(TimeSpan.FromMinutes(1));
        var timeout = CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed: {FailureReason}", "device-1", "timeout");
        var authentication = CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed: {FailureReason}", "device-1", "authentication");

        filter.IsEnabled(timeout).Should().BeTrue();
        filter.IsEnabled(authentication).Should().BeTrue();
        filter.IsEnabled(CreateEvent(LogEventLevel.Information, "Device {SourceId} poll recovered", "device-1")).Should().BeTrue();

        filter.IsEnabled(timeout).Should().BeTrue();
        filter.IsEnabled(authentication).Should().BeTrue();
    }

    [TestMethod]
    public void IsEnabled_DoesNotConflateDifferentFailureReasons()
    {
        var filter = new RepeatedFailureFilter(TimeSpan.FromMinutes(1));

        filter.IsEnabled(CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed: {FailureReason}", "device-1", "timeout")).Should().BeTrue();
        filter.IsEnabled(CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed: {FailureReason}", "device-1", "authentication")).Should().BeTrue();
    }

    [TestMethod]
    public void IsEnabled_BoundsTrackedFailuresUnderConcurrency()
    {
        var filter = new RepeatedFailureFilter(TimeSpan.FromMinutes(1));

        Parallel.For(0, 2048, index =>
            filter.IsEnabled(CreateEvent(LogEventLevel.Warning, "Device {SourceId} poll failed", $"device-{index}")));

        filter.TrackedFailureCount.Should().BeLessThanOrEqualTo(1024);
    }

    private static LogEvent CreateEvent(
        LogEventLevel level,
        string template,
        string? sourceId = null,
        string? failureReason = null) =>
        new(
            DateTimeOffset.UtcNow,
            level,
            exception: null,
            new MessageTemplateParser().Parse(template),
            CreateProperties(sourceId, failureReason));

    private static IReadOnlyList<LogEventProperty> CreateProperties(string? sourceId, string? failureReason)
    {
        var properties = new List<LogEventProperty>();
        if (sourceId is not null)
            properties.Add(new LogEventProperty("SourceId", new ScalarValue(sourceId)));
        if (failureReason is not null)
            properties.Add(new LogEventProperty("FailureReason", new ScalarValue(failureReason)));
        return properties;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}

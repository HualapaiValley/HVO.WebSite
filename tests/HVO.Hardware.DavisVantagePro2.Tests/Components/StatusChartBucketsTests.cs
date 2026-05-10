using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Components.Pages;

namespace HVO.Hardware.DavisVantagePro2.Tests.Components;

[TestClass]
public class StatusChartBucketsTests
{
    [TestMethod]
    public void BuildBuckets_InsertsNullEntriesForMissingMinutes()
    {
        DateTime windowStart = new(2026, 5, 9, 12, 0, 0, DateTimeKind.Unspecified);
        DateTime windowEnd = new(2026, 5, 9, 12, 4, 0, DateTimeKind.Unspecified);
        StatusChartPoint[] points =
        [
            new(new DateTime(2026, 5, 9, 12, 0, 15), 10),
            new(new DateTime(2026, 5, 9, 12, 2, 10), 12),
            new(new DateTime(2026, 5, 9, 12, 4, 55), 15)
        ];

        var buckets = StatusChartBuckets.BuildBuckets(points, windowStart, windowEnd, StatusChartBuckets.DefaultBucketSize);

        buckets.Should().HaveCount(5);
        buckets[0]!.TimeLocal.Should().Be(windowStart);
        buckets[0]!.Value.Should().Be(10);
        buckets[1].Should().BeNull();
        buckets[2]!.TimeLocal.Should().Be(windowStart.AddMinutes(2));
        buckets[2]!.Value.Should().Be(12);
        buckets[3].Should().BeNull();
        buckets[4]!.TimeLocal.Should().Be(windowStart.AddMinutes(4));
        buckets[4]!.Value.Should().Be(15);
    }

    [TestMethod]
    public void BuildBuckets_UsesLatestValueWithinEachMinuteBucket()
    {
        DateTime windowStart = new(2026, 5, 9, 8, 30, 0, DateTimeKind.Unspecified);
        DateTime windowEnd = new(2026, 5, 9, 8, 31, 0, DateTimeKind.Unspecified);
        StatusChartPoint[] points =
        [
            new(new DateTime(2026, 5, 9, 8, 30, 5), 71.2),
            new(new DateTime(2026, 5, 9, 8, 30, 50), 72.8),
            new(new DateTime(2026, 5, 9, 8, 31, 5), 73.1)
        ];

        var buckets = StatusChartBuckets.BuildBuckets(points, windowStart, windowEnd, StatusChartBuckets.DefaultBucketSize);

        buckets.Should().HaveCount(2);
        buckets[0]!.Value.Should().Be(72.8);
        buckets[1]!.Value.Should().Be(73.1);
    }
}
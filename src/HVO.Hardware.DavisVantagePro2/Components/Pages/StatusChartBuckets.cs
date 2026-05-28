namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

internal static class StatusChartBuckets
{
    internal static readonly TimeSpan DefaultBucketSize = TimeSpan.FromMinutes(1);

    internal static IReadOnlyList<StatusChartBucket?> BuildBuckets(
        IEnumerable<StatusChartPoint> points,
        DateTime windowStart,
        DateTime windowEnd,
        TimeSpan bucketSize)
    {
        if (bucketSize <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSize), "Bucket size must be positive.");
        }

        DateTime alignedStart = FloorToBucket(windowStart, bucketSize);
        DateTime alignedEnd = FloorToBucket(windowEnd, bucketSize);

        if (alignedEnd < alignedStart)
        {
            return [];
        }

        var pointsByBucket = points
            .OrderBy(point => point.TimeLocal)
            .GroupBy(point => FloorToBucket(point.TimeLocal, bucketSize))
            .ToDictionary(
                group => group.Key,
                group => new StatusChartBucket(group.Key, group.Last().Value));

        var buckets = new List<StatusChartBucket?>();
        for (var bucket = alignedStart; bucket <= alignedEnd; bucket = bucket.Add(bucketSize))
        {
            buckets.Add(pointsByBucket.TryGetValue(bucket, out var point) ? point : null);
        }

        return buckets;
    }

    internal static DateTime FloorToBucket(DateTime value, TimeSpan bucketSize)
    {
        long bucketTicks = bucketSize.Ticks;
        long flooredTicks = value.Ticks - (value.Ticks % bucketTicks);
        return new DateTime(flooredTicks, value.Kind);
    }
}

internal sealed record StatusChartPoint(DateTime TimeLocal, double Value);

internal sealed record StatusChartBucket(DateTime TimeLocal, double Value);
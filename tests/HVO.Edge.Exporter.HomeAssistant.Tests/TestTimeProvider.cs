namespace HVO.Edge.Exporter.HomeAssistant.Tests;

internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset utcNow = now;
    private long timestamp;
    public override DateTimeOffset GetUtcNow() => utcNow;
    public override long GetTimestamp() => timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan elapsed)
    {
        utcNow += elapsed;
        timestamp += elapsed.Ticks;
    }
}

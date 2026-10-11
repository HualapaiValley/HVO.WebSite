namespace HVO.WebSite.v9.Models;

/// <summary>A positive, half-open UTC window of at most 24 hours.</summary>
public readonly record struct WeatherHistoryRange(DateTimeOffset Start, DateTimeOffset End)
{
    public static readonly TimeSpan MaximumSpan = TimeSpan.FromHours(24);

    public static WeatherHistoryRange Create(DateTimeOffset? start, DateTimeOffset? end, TimeProvider clock)
    {
        try
        {
            var endUtc = end?.ToUniversalTime() ?? (start.HasValue
                ? start.Value.ToUniversalTime().Add(MaximumSpan) : clock.GetUtcNow());
            var startUtc = start?.ToUniversalTime() ?? endUtc.Subtract(MaximumSpan);
            if (endUtc <= startUtc || endUtc - startUtc > MaximumSpan)
                throw new ArgumentException("The end must follow the start by no more than 24 hours.");
            return new(startUtc, endUtc);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException("The requested 24-hour window is outside the supported date range.");
        }
    }
}

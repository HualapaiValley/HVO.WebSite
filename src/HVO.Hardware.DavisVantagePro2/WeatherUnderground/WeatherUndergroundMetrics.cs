using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HVO.Hardware.DavisVantagePro2.WeatherUnderground;

internal sealed class WeatherUndergroundMetrics : IDisposable
{
    public const string MeterName = "HVO.Hardware.DavisVantagePro2.WeatherUnderground";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> attempts;
    private readonly Counter<long> outcomes;
    private readonly Histogram<double> duration;

    public WeatherUndergroundMetrics()
    {
        attempts = meter.CreateCounter<long>("davis.weather_underground.send.attempts", "{attempt}");
        outcomes = meter.CreateCounter<long>("davis.weather_underground.send.outcomes", "{delivery}");
        duration = meter.CreateHistogram<double>("davis.weather_underground.send.duration", "s");
    }

    public void RecordAttempt(string stationId) => attempts.Add(1, new KeyValuePair<string, object?>("station.id", stationId));

    public void RecordOutcome(string stationId, WeatherUndergroundOutcome outcome, double durationSeconds = 0)
    {
        var tags = new TagList
        {
            { "station.id", stationId },
            { "outcome", outcome.Category() },
        };
        outcomes.Add(1, tags);
        if (durationSeconds > 0)
            duration.Record(durationSeconds, tags);
    }

    public void Dispose() => meter.Dispose();
}

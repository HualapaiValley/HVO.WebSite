using System.Diagnostics.Metrics;

namespace HVO.WebSite.v9.Telemetry;

/// <summary>Custom metrics for website power ingest. Exported by the website OpenTelemetry pipeline.</summary>
public sealed class PowerIngestTelemetry : IDisposable
{
    public const string MeterName = "hvo.website.power";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _records;
    private readonly Histogram<int> _batchSize;
    private readonly Histogram<double> _batchDurationMs;

    public PowerIngestTelemetry()
    {
        _records = _meter.CreateCounter<long>(
            "hvo.power.ingest.records",
            unit: "records",
            description: "Power ingest records by result.");
        _batchSize = _meter.CreateHistogram<int>(
            "hvo.power.ingest.batch.size",
            unit: "records",
            description: "Power ingest request batch size.");
        _batchDurationMs = _meter.CreateHistogram<double>(
            "hvo.power.ingest.batch.duration",
            unit: "ms",
            description: "Power ingest batch processing duration.");
    }

    public void RecordBatch(int batchSize, int inserted, int skipped, int failed, double durationMs)
    {
        _batchSize.Record(batchSize);
        _batchDurationMs.Record(durationMs);
        if (inserted > 0) _records.Add(inserted, new KeyValuePair<string, object?>("result", "inserted"));
        if (skipped > 0) _records.Add(skipped, new KeyValuePair<string, object?>("result", "skipped"));
        if (failed > 0) _records.Add(failed, new KeyValuePair<string, object?>("result", "failed"));
    }

    public void Dispose() => _meter.Dispose();
}

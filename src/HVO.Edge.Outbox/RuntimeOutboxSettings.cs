namespace HVO.Edge.Outbox;

public sealed class RuntimeOutboxSettings
{
    private int? _batchSizeOverride;
    private int? _sweepIntervalSecondsOverride;

    private static readonly int BatchSizeLower = 1;
    private static readonly int BatchSizeUpper = 500;
    private static readonly int SweepIntervalLower = 1;
    private static readonly int SweepIntervalUpper = 60;

    public int? BatchSizeOverride
    {
        get => _batchSizeOverride;
        set
        {
            if (value is { } v && (v < BatchSizeLower || v > BatchSizeUpper))
                throw new ArgumentOutOfRangeException(
                    nameof(value), value,
                    $"Batch size must be between {BatchSizeLower} and {BatchSizeUpper}.");
            _batchSizeOverride = value;
        }
    }

    public int? SweepIntervalSecondsOverride
    {
        get => _sweepIntervalSecondsOverride;
        set
        {
            if (value is { } v && (v < SweepIntervalLower || v > SweepIntervalUpper))
                throw new ArgumentOutOfRangeException(
                    nameof(value), value,
                    $"Sweep interval must be between {SweepIntervalLower} and {SweepIntervalUpper} seconds.");
            _sweepIntervalSecondsOverride = value;
        }
    }

    public int EffectiveBatchSize(int configured) => BatchSizeOverride ?? configured;
    public int EffectiveSweepIntervalSeconds(int configured) => SweepIntervalSecondsOverride ?? configured;

    public void Reset()
    {
        _batchSizeOverride = null;
        _sweepIntervalSecondsOverride = null;
    }
}

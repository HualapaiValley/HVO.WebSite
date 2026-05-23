namespace HVO.Gateway.SolarAssistant.SolarAssistant;

public sealed class SolarAssistantMetricInventory
{
    public DateTime RecordedAtUtc { get; init; }

    public int MetricCount { get; init; }

    public IReadOnlyList<SolarAssistantMetricSummary> Topics { get; init; } = [];

    public IReadOnlyDictionary<string, int> PrefixCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> GroupCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> UnitCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> ClassificationCounts { get; init; } = new Dictionary<string, int>();
}

public sealed class SolarAssistantMetricSummary
{
    public string Topic { get; init; } = string.Empty;

    public string? Group { get; init; }

    public string? Name { get; init; }

    public string? Unit { get; init; }

    public string Classification { get; init; } = SolarAssistantMetricClassification.LocalOnly;
}

public static class SolarAssistantMetricClassification
{
    public const string DbCandidate = "db_candidate";
    public const string Review = "review";
    public const string LocalOnly = "local_only";
}

public static class SolarAssistantMetricInventoryBuilder
{
    private static readonly HashSet<string> DbCandidateTopics = new(StringComparer.OrdinalIgnoreCase)
    {
        "total/pv_power",
        "total/load_power",
        "total/grid_power",
        "total/battery_power",
        "total/system_power",
        "total/power",
        "total/battery_state_of_charge",
        "total/battery_voltage",
        "total/battery_current",
        "total/battery_capacity",
        "total/grid_voltage",
        "total/grid_frequency",
        "total/ac_output_voltage",
        "total/ac_output_frequency",
        "total/load_percentage",
        "total/inverter_mode",
        "total/output_source_priority",
        "battery_1/voltage",
        "battery_1/current",
        "battery_1/capacity",
        "inverter_1/grid_voltage",
        "inverter_1/grid_frequency",
        "inverter_1/ac_output_voltage",
        "inverter_1/ac_output_frequency",
        "inverter_1/output_voltage",
        "inverter_1/output_frequency",
        "inverter_1/load_percentage",
        "inverter_1/device_mode",
        "inverter_1/output_source_priority",
        "inverter_1/charger_source_priority",
    };

    private static readonly string[] LocalOnlyFragments =
    [
        "firmware",
        "model",
        "serial",
        "status_",
        "temperature",
        "version",
    ];

    private static readonly string[] ReviewFragments =
    [
        "battery",
        "buzzer",
        "charge",
        "current",
        "energy",
        "frequency",
        "grid",
        "load",
        "mode",
        "operation",
        "power",
        "priority",
        "pv",
        "voltage",
    ];

    public static SolarAssistantMetricInventory Build(
        IReadOnlyList<SolarAssistantMetric> metrics,
        DateTime recordedAtUtc)
    {
        var topics = metrics
            .Where(m => !string.IsNullOrWhiteSpace(m.Topic))
            .Select(m => new SolarAssistantMetricSummary
            {
                Topic = NormalizeTopic(m.Topic),
                Group = NormalizeOptional(m.Group),
                Name = NormalizeOptional(m.Name),
                Unit = NormalizeOptional(m.Unit),
                Classification = Classify(m),
            })
            .GroupBy(m => m.Topic, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(m => m.Topic, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SolarAssistantMetricInventory
        {
            RecordedAtUtc = recordedAtUtc.ToUniversalTime(),
            MetricCount = metrics.Count,
            Topics = topics,
            PrefixCounts = CountBy(topics.Select(t => t.Topic.Split('/', 2)[0])),
            GroupCounts = CountBy(topics.Select(t => t.Group)),
            UnitCounts = CountBy(topics.Select(t => t.Unit).Where(u => !string.IsNullOrWhiteSpace(u))!),
            ClassificationCounts = CountBy(topics.Select(t => t.Classification)),
        };
    }

    public static string Classify(SolarAssistantMetric metric)
    {
        var topic = NormalizeTopic(metric.Topic);
        if (DbCandidateTopics.Contains(topic))
            return SolarAssistantMetricClassification.DbCandidate;

        if (ContainsAny(topic, LocalOnlyFragments))
            return SolarAssistantMetricClassification.LocalOnly;

        if (IsPowerSystemUnit(metric.Unit) || ContainsAny(topic, ReviewFragments) || ContainsAny(metric.Name ?? string.Empty, ReviewFragments))
            return SolarAssistantMetricClassification.Review;

        return SolarAssistantMetricClassification.LocalOnly;
    }

    private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string?> values) => values
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    private static string NormalizeTopic(string topic) => topic.Trim().Trim('/');

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ContainsAny(string value, IEnumerable<string> fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool IsPowerSystemUnit(string? unit) => unit is "W" or "Wh" or "kWh" or "V" or "A" or "Hz" or "%" or "VA";
}

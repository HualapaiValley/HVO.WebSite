using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.JkBms.Outbox;

/// <summary>
/// Configuration for the outbox forwarder that POSTs BMS readings to the web API.
/// Bound from the "Outbox" configuration section.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>HTTP endpoint that accepts the batched BMS reading payload.</summary>
    [Required]
    public string ApiEndpoint { get; set; } = "https://localhost:5001/api/v1/bms/raw";

    /// <summary>
    /// API key sent in the <c>X-Api-Key</c> header on every POST.
    /// Leave empty or as the placeholder to disable forwarding in development.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maximum number of delivery attempts before a record is marked Failed.</summary>
    [Range(1, 100)]
    public int MaxRetryAttempts { get; set; } = 10;

    /// <summary>Cap on exponential backoff delay between retries (seconds).</summary>
    [Range(10, 3600)]
    public int MaxBackoffSeconds { get; set; } = 300;

    /// <summary>How often the forwarder sweeps pending records (seconds).</summary>
    [Range(1, 60)]
    public int SweepIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum records forwarded to the API in a single batch POST.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Warn when pending outbox records exceed this count. Set to 0 to warn on any pending record.</summary>
    [Range(0, 100000)]
    public int PendingWarningCount { get; set; } = 10;

    /// <summary>Classify historical failed outbox records as over-threshold at this count. Historical failures warn/degrade; current forwarding failures are critical. Set to 0 to disable.</summary>
    [Range(0, 100000)]
    public int FailedCriticalCount { get; set; } = 1;

    /// <summary>
    /// Path to the SQLite database file.
    /// Defaults to <c>outbox.db</c> in the content root when empty.
    /// In Docker this should be set to <c>/app/data/outbox.db</c>.
    /// </summary>
    public string DbPath { get; set; } = string.Empty;

    /// <summary>
    /// Number of days to retain <see cref="OutboxStatus.Sent"/> records before they are
    /// purged by the compaction task. Set to 0 to disable compaction.
    /// Defaults to 7 days.
    /// </summary>
    [Range(0, 3650)]
    public int SentRetentionDays { get; set; } = 7;
}

using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.DavisVantagePro2.Configuration;

/// <summary>Strongly-typed configuration options for the Davis station worker.</summary>
public sealed class StationOptions
{
    public const string SectionName = "Station";

    [Required]
    public string Host { get; set; } = "192.168.2.121";

    [Range(1, 65535)]
    public int Port { get; set; } = 22222;

    [Range(4, 60)]
    public int SocketTimeoutSeconds { get; set; } = 8;

    /// <summary>Controls whether startup archive catchup is disabled, conditional, or always runs.</summary>
    public ArchiveCatchupMode ArchiveCatchupMode { get; set; } = ArchiveCatchupMode.Disabled;

    /// <summary>When mode is Enabled, only run catchup if the latest persisted reading is older than this many hours.</summary>
    [Range(1, 168)]
    public int ArchiveCatchupLookbackHours { get; set; } = 12;

    /// <summary>How many consecutive errors before the worker pauses and retries the connection.</summary>
    [Range(1, 100)]
    public int MaxConsecutiveErrors { get; set; } = 5;

    /// <summary>Station identifier sent to the API with each reading (e.g. "hvo-davis-01").</summary>
    [Required]
    public string StationId { get; set; } = "hvo-davis-01";
}

public enum ArchiveCatchupMode
{
    Disabled,
    Enabled,
    Force,
}

/// <summary>Configuration for the outbox forwarder that POSTs readings to the web API.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Required, Url]
    public string ApiEndpoint { get; set; } = "http://localhost:5001/api/v1/weather/raw";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 100)]
    public int MaxRetryAttempts { get; set; } = 10;

    /// <summary>Cap on exponential backoff delay (seconds).</summary>
    [Range(10, 3600)]
    public int MaxBackoffSeconds { get; set; } = 300;

    /// <summary>How often the forwarder sweeps pending records (seconds).</summary>
    [Range(1, 60)]
    public int SweepIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum number of records sent to the API in a single batch POST.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    [Range(0, 3650)]
    public int FailedRetentionDays { get; set; } = 30;

    [Range(0, 100000)]
    public int PendingWarningCount { get; set; } = 10;

    [Range(0, 100000)]
    public int FailedCriticalCount { get; set; } = 1;

    /// <summary>Path to the SQLite database file. Defaults to local application data when empty.</summary>
    public string DbPath { get; set; } = string.Empty;
}

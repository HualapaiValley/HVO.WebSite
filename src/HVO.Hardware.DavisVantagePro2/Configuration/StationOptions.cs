using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.DavisVantagePro2.Configuration;

/// <summary>Strongly-typed configuration options for the Davis station worker.</summary>
public sealed class StationOptions
{
    public const string SectionName = "Station";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 22222;

    [Range(4, 60)]
    public int SocketTimeoutSeconds { get; set; } = 8;

    /// <summary>Controls whether periodic archive catchup is disabled, conditional, or always runs.</summary>
    public ArchiveCatchupMode ArchiveCatchupMode { get; set; } = ArchiveCatchupMode.Disabled;

    [Range(1, 12)]
    public int ArchiveOverlapIntervals { get; set; } = 2;

    /// <summary>How many consecutive errors before the worker pauses and retries the connection.</summary>
    [Range(1, 100)]
    public int MaxConsecutiveErrors { get; set; } = 5;

    /// <summary>Station identifier sent to the API with each reading (e.g. "hvo-davis-01").</summary>
    [Required]
    public string StationId { get; set; } = string.Empty;

    public string? CentralIngestBaseEndpoint { get; set; }
    public string CentralApiKeySecret { get; set; } = "central-ingest-api-key";
    public bool AllowInsecureCentralIngest { get; set; }
    public double? LegacyArchiveConsoleUtcOffsetHours { get; set; }
    [Range(1, 1440)] public int RetryExhaustedRequeueMinutes { get; set; } = 15;
    [Required] public string LocalDatabasePath { get; set; } = "/app/data/davis-local.db";
}

public enum ArchiveCatchupMode
{
    Disabled,
    Enabled,
    Force,
}

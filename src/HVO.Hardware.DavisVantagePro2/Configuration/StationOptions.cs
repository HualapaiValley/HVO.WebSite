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

    /// <summary>How often to request a LOOP2 packet (seconds). Minimum 5.</summary>
    [Range(5, 3600)]
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>Run DMPAFT on startup to catch up any missed archive records.</summary>
    public bool ArchiveCatchupOnStartup { get; set; } = true;

    /// <summary>How many consecutive errors before the worker pauses and retries the connection.</summary>
    [Range(1, 100)]
    public int MaxConsecutiveErrors { get; set; } = 5;
}

/// <summary>Configuration for the outbox forwarder that POSTs readings to the web API.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Required, Url]
    public string ApiEndpoint { get; set; } = "https://localhost:5001/api/v1/weather/raw";

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
}

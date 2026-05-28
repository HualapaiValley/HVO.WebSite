using System.ComponentModel.DataAnnotations;

namespace HVO.Gateway.SolarAssistant.Configuration;

/// <summary>Configuration for forwarding local power readings to the HVO website API.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Website endpoint that accepts batched power readings.</summary>
    [Required]
    public string ApiEndpoint { get; set; } = "https://localhost:5001/api/v1/power/readings";

    /// <summary>API key sent in the X-Api-Key header. Leave empty or REPLACE_ME to disable forwarding.</summary>
    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 100)]
    public int MaxRetryAttempts { get; set; } = 10;

    [Range(10, 3600)]
    public int MaxBackoffSeconds { get; set; } = 300;

    [Range(1, 60)]
    public int SweepIntervalSeconds { get; set; } = 5;

    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Path to the SQLite database file. Defaults to outbox.db in the content root.</summary>
    public string DbPath { get; set; } = string.Empty;

    [Range(0, 3650)]
    public int SentRetentionDays { get; set; } = 7;
}

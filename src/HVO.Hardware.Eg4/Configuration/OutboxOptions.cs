using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.Eg4.Configuration;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";
    [Required] public string ApiEndpoint { get; set; } = "https://localhost:5001/api/v1/power/readings";
    public string ApiKey { get; set; } = string.Empty;
    [Range(1, 100)] public int MaxRetryAttempts { get; set; } = 10;
    [Range(10, 3600)] public int MaxBackoffSeconds { get; set; } = 300;
    [Range(1, 60)] public int SweepIntervalSeconds { get; set; } = 5;
    [Range(1, 500)] public int BatchSize { get; set; } = 50;
    public string DbPath { get; set; } = string.Empty;
    [Range(0, 3650)] public int SentRetentionDays { get; set; } = 7;
    [Range(0, 3650)] public int FailedRetentionDays { get; set; } = 30;
    [Range(0, 100000)] public int PendingWarningCount { get; set; } = 10;
    [Range(0, 100000)] public int FailedCriticalCount { get; set; } = 1;
}

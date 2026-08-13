using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.VictronSmartShunt.Configuration;

public sealed class SmartShuntOptions
{
    public const string SectionName = "SmartShunt";

    [Required] public string Address { get; set; } = string.Empty;
    [Required] public string Adapter { get; set; } = "hci0";
    [Required, MaxLength(64)] public string SourceId { get; set; } = "smartshunt-main";
    [Required, MaxLength(64)] public string DeviceId { get; set; } = "smartshunt-lifepo4";
    public string? CentralIngestBaseEndpoint { get; set; }
    public string CentralApiKeySecret { get; set; } = "central-ingest-api-key";
    public bool AllowInsecureCentralIngest { get; set; }
    [Range(1, 3600)] public int SampleIntervalSeconds { get; set; } = 5;
    [Range(1, 3600)] public int SnapshotIntervalSeconds { get; set; } = 15;
    [Range(1, 3600)] public int PublicKeepAliveIntervalSeconds { get; set; } = 10;
    [Range(1, 300)] public int ConnectionTimeoutSeconds { get; set; } = 20;
    [Range(5, 3600)] public int SampleStaleAfterSeconds { get; set; } = 60;
    [Range(1, 1440)] public int RetryExhaustedRequeueMinutes { get; set; } = 15;
}

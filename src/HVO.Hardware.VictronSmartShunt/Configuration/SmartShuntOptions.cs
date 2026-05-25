using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.VictronSmartShunt.Configuration;

public sealed class SmartShuntOptions
{
    public const string SectionName = "SmartShunt";

    public string Address { get; set; } = string.Empty;

    [Required]
    public string Adapter { get; set; } = "hci0";

    [Required]
    public string SourceId { get; set; } = "smartshunt-main";

    [Required]
    public string DeviceId { get; set; } = "smartshunt-lifepo4";

    public bool PublicOnly { get; set; } = true;

    public bool EnablePrivateEnrichment { get; set; }

    [Range(1, 3600)]
    public int SampleIntervalSeconds { get; set; } = 5;

    [Range(1, 3600)]
    public int SnapshotIntervalSeconds { get; set; } = 15;

    [Range(1, 3600)]
    public int PublicKeepAliveIntervalSeconds { get; set; } = 10;

    [Range(1, 86400)]
    public int PrivateRefreshIntervalSeconds { get; set; } = 900;

    [Range(1, 5000)]
    public int HistoryCapacity { get; set; } = 240;

    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 20;

    [Range(5, 3600)]
    public int SampleStaleAfterSeconds { get; set; } = 60;

    [Range(0, 100000)]
    public int OutboxPendingWarningCount { get; set; } = 10;

    [Range(0, 100000)]
    public int OutboxFailedCriticalCount { get; set; } = 1;

    [Range(0, 100)]
    public double LowBatteryWarningPercent { get; set; } = 30;

    [Range(0, 100)]
    public double CriticalBatteryPercent { get; set; } = 15;
}

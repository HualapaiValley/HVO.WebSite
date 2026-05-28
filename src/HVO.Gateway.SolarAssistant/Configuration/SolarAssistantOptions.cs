using System.ComponentModel.DataAnnotations;

namespace HVO.Gateway.SolarAssistant.Configuration;

/// <summary>Configuration for read-only SolarAssistant REST snapshot polling.</summary>
public sealed class SolarAssistantOptions
{
    public const string SectionName = "SolarAssistant";

    /// <summary>SolarAssistant host name or IP address. Leave empty to disable polling.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>HTTP port for the local SolarAssistant REST API.</summary>
    [Range(1, 65535)]
    public int RestPort { get; set; } = 80;

    /// <summary>REST API user name for HTTP Basic authentication.</summary>
    public string RestUsername { get; set; } = "admin";

    /// <summary>REST API password for HTTP Basic authentication.</summary>
    public string RestPassword { get; set; } = string.Empty;

    /// <summary>Optional bearer token alternative to Basic authentication.</summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>Stable source id for the aggregate total power snapshot.</summary>
    [Required]
    [MaxLength(64)]
    public string TotalSourceId { get; set; } = "solarassistant-total";

    /// <summary>Logical device id for aggregate total snapshots.</summary>
    [Required]
    [MaxLength(64)]
    public string TotalDeviceId { get; set; } = "total";

    /// <summary>Snapshot poll interval in seconds.</summary>
    [Range(5, 3600)]
    public int SnapshotIntervalSeconds { get; set; } = 30;

    /// <summary>Number of recent snapshots retained in memory for the local monitor charts.</summary>
    [Range(2, 2880)]
    public int HistoryCapacity { get; set; } = 240;

    /// <summary>HTTP timeout for SolarAssistant REST calls.</summary>
    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>Enables read-only MQTT discovery/state subscriptions.</summary>
    public bool EnableMqttDiscovery { get; set; } = true;

    /// <summary>MQTT port for the local SolarAssistant broker.</summary>
    [Range(1, 65535)]
    public int MqttPort { get; set; } = 1883;

    /// <summary>MQTT user name. Keep in local environment/secrets.</summary>
    public string MqttUsername { get; set; } = string.Empty;

    /// <summary>MQTT password. Keep in local environment/secrets.</summary>
    public string MqttPassword { get; set; } = string.Empty;

    /// <summary>Stable MQTT client id prefix used for the read-only subscriber.</summary>
    [MaxLength(64)]
    public string MqttClientId { get; set; } = "hvo-solarassistant-gateway";

    /// <summary>Delay before reconnecting MQTT after disconnect/auth/network errors.</summary>
    [Range(1, 3600)]
    public int MqttReconnectDelaySeconds { get; set; } = 15;

    /// <summary>Warn when the latest REST snapshot is older than this many seconds.</summary>
    [Range(10, 86400)]
    public int RestStaleAfterSeconds { get; set; } = 120;

    /// <summary>Warn when MQTT is connected but no state/discovery message has arrived within this many seconds.</summary>
    [Range(10, 86400)]
    public int MqttStaleAfterSeconds { get; set; } = 120;

    /// <summary>Warn when pending outbox records exceed this count. Set to 0 to warn on any pending record.</summary>
    [Range(0, 100000)]
    public int OutboxPendingWarningCount { get; set; } = 10;

    /// <summary>Classify historical failed outbox records as over-threshold at this count. Historical failures warn/degrade; current forwarding failures are critical. Set to 0 to disable.</summary>
    [Range(0, 100000)]
    public int OutboxFailedCriticalCount { get; set; } = 1;

    /// <summary>Warn when battery state of charge is at or below this percentage.</summary>
    [Range(0, 100)]
    public double LowBatteryWarningPercent { get; set; } = 30;

    /// <summary>Raise a critical local alert when battery state of charge is at or below this percentage.</summary>
    [Range(0, 100)]
    public double CriticalBatteryPercent { get; set; } = 15;

    /// <summary>Warn when load power is at or above this threshold in watts. Set to 0 to disable.</summary>
    [Range(0, 1000000)]
    public double HighLoadWarningW { get; set; } = 5000;

    /// <summary>Warn when battery discharge power is at or above this threshold in watts. Set to 0 to disable.</summary>
    [Range(0, 1000000)]
    public double BatteryDischargeWarningW { get; set; } = 5000;
}

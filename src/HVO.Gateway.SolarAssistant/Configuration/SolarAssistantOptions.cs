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

    /// <summary>HTTP timeout for SolarAssistant REST calls.</summary>
    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 10;
}

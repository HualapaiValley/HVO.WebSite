using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.JkBms.Configuration;

/// <summary>
/// Per-device configuration for a single JK BMS unit.
/// Each entry in <see cref="JkBmsOptions.Devices"/> represents one physical BMS.
/// </summary>
public sealed class BmsDeviceConfig
{
    /// <summary>
    /// Bluetooth MAC address of the device (e.g. "C8:47:8C:E4:58:37").
    /// Used to construct the BlueZ D-Bus device path.
    /// </summary>
    [Required]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable label used in logs, the status UI, and outbox payloads.
    /// Example: "battery-bank-1a"
    /// </summary>
    [Required]
    public string Alias { get; set; } = string.Empty;

    /// <summary>Stable non-secret identity used for Home Assistant entities.</summary>
    [Required]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Per-device poll interval override (seconds).
    /// If zero or not set, the global <see cref="JkBmsOptions.DefaultPollIntervalSeconds"/> is used.
    /// </summary>
    [Range(0, 3600)]
    public int PollIntervalSeconds { get; set; }

    /// <summary>
    /// HCI adapter to use for this device (e.g. "hci0", "hci1").
    /// When null or empty, falls back to <see cref="JkBmsOptions.HciAdapter"/>.
    /// Use this to distribute devices across multiple Bluetooth dongles.
    /// </summary>
    public string? HciAdapter { get; set; }

    /// <summary>
    /// Optional file name under the configured secrets directory containing a new
    /// six-digit settings password. When configured, Home Assistant exposes a one-shot button.
    /// </summary>
    public string? SettingsPasswordSecret { get; set; }

    /// <summary>
    /// Whether this device is enabled. Set to false to temporarily skip a device
    /// without removing it from configuration.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

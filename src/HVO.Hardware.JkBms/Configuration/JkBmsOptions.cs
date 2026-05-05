using System.ComponentModel.DataAnnotations;

namespace HVO.Hardware.JkBms.Configuration;

/// <summary>
/// Global options for the JK BMS poller service.
/// Bound from the "JkBms" configuration section.
/// </summary>
public sealed class JkBmsOptions
{
    public const string SectionName = "JkBms";

    /// <summary>
    /// BLE connect + GATT resolve timeout per attempt (seconds).
    /// </summary>
    [Range(5, 120)]
    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Timeout for a single BLE request/response exchange (seconds).
    /// Kept shorter than <see cref="ConnectTimeoutSeconds"/> so a dropped connection is
    /// detected quickly rather than waiting for the full connect budget to expire.
    /// Also used as the timeout for the post-connect device-info init (0x97) exchange.
    /// </summary>
    [Range(1, 60)]
    public int ExchangeTimeoutSeconds { get; set; } = 10;

    /// <summary>Default poll interval for devices that do not override it (seconds).</summary>
    [Range(10, 3600)]
    public int DefaultPollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Default BlueZ HCI adapter (e.g. "hci0"). Each device may override this
    /// via <see cref="BmsDeviceConfig.HciAdapter"/> to distribute load across
    /// multiple Bluetooth dongles.
    /// </summary>
    public string HciAdapter { get; set; } = "hci0";

    /// <summary>Configured BMS devices to poll.</summary>
    [Required, MinLength(1)]
    public List<BmsDeviceConfig> Devices { get; set; } = [];
}

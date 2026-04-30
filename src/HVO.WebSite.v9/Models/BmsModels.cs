using System.ComponentModel.DataAnnotations;

namespace HVO.WebSite.v9.Models;

// ── Ingest request ────────────────────────────────────────────────────────────

/// <summary>
/// Top-level ingest payload for a single BMS poll cycle.
/// Mirrors <c>BmsIngressRecord</c> from the hardware project.
/// </summary>
public class BmsIngestRequest
{
    [Required]
    public required BmsReadingRequest Reading { get; init; }

    /// <summary>Non-null only when config has changed since last send.</summary>
    public BmsConfigRequest? Config { get; init; }

    /// <summary>Non-null only when device info has changed since last send.</summary>
    public BmsDeviceInfoRequest? DeviceInfo { get; init; }
}

public class BmsReadingRequest
{
    [Required]
    [MaxLength(17)]
    public required string DeviceAddress { get; init; }

    [Required]
    [MaxLength(100)]
    public required string DeviceAlias { get; init; }

    public DateTime RecordedAtUtc { get; init; }

    // Pack electrical
    public long PackVoltageMv { get; init; }

    /// <remarks>
    /// The hardware project uses <c>TotalVoltageMv</c> (uint) as the field name.
    /// The ingress payload contains either name; prefer PackVoltageMv.
    /// </remarks>
    public long? TotalVoltageMv { get; init; }

    public int CurrentMa { get; init; }

    // State
    [Range(0, 100)]
    public int SocPercent { get; init; }

    [Range(0, 100)]
    public int SohPercent { get; init; }

    // Capacity
    public long RemainingCapacityMah { get; init; }
    public long NominalCapacityMah { get; init; }
    public long CycleCount { get; init; }
    public long CycleCapacityMah { get; init; }

    // Temperatures
    [Range(-100.0, 200.0)]
    public double BatteryTemperature1C { get; init; }

    [Range(-100.0, 200.0)]
    public double BatteryTemperature2C { get; init; }

    [Range(-100.0, 200.0)]
    public double PowerTubeTemperatureC { get; init; }

    // Balancing
    public bool BalancingActive { get; init; }
    public double BalancingCurrentMa { get; init; }

    // Cell delta
    public int DeltaCellVoltageMv { get; init; }

    // Alarms
    public long AlarmBitmask { get; init; }

    // Per-cell arrays (optional — omit to skip cell rows)
    public IReadOnlyList<int>? CellVoltagesMv { get; init; }
    public IReadOnlyList<int>? CellResistancesMOhm { get; init; }
}

public class BmsConfigRequest
{
    public byte CellCount { get; init; }
    public long NominalCapacityMah { get; init; }
    public bool ChargingEnabled { get; init; }
    public bool DischargingEnabled { get; init; }
    public bool BalancingEnabled { get; init; }
    public long CellOvpMv { get; init; }
    public long CellOvpRecoveryMv { get; init; }
    public long CellUvpMv { get; init; }
    public long CellUvpRecoveryMv { get; init; }
    public long BalanceTriggerMv { get; init; }
    public long BalanceStartVoltageMv { get; init; }
    public long ChargeOcpMa { get; init; }
    public long ChargeOcpDelayS { get; init; }
    public long ChargeOcpRecoveryS { get; init; }
    public long DischargeOcpMa { get; init; }
    public long DischargeOcpDelayS { get; init; }
    public long DischargeOcpRecoveryS { get; init; }
    public long ShortCircuitDelayUs { get; init; }
    public long ShortCircuitRecoveryS { get; init; }
    public double ChargeOtpC { get; init; }
    public double ChargeOtpRecoveryC { get; init; }
    public double ChargeUtpC { get; init; }
    public double ChargeUtpRecoveryC { get; init; }
    public double DischargeOtpC { get; init; }
    public double DischargeOtpRecoveryC { get; init; }
    public double MosOtpC { get; init; }
    public double MosOtpRecoveryC { get; init; }
}

public class BmsDeviceInfoRequest
{
    [MaxLength(100)]
    public string? Manufacturer { get; init; }

    [MaxLength(50)]
    public string? Hardware { get; init; }

    [MaxLength(50)]
    public string? Firmware { get; init; }

    [MaxLength(50)]
    public string? SerialNumber { get; init; }

    [MaxLength(100)]
    public string? DeviceName { get; init; }

    [MaxLength(20)]
    public string? ManufacturingDate { get; init; }

    [MaxLength(200)]
    public string? UserData { get; init; }
}

// ── Ingest response ───────────────────────────────────────────────────────────

/// <summary>Response body returned from the batch BMS ingest endpoint.</summary>
public class BmsIngestBatchResponse
{
    /// <summary>Number of reading rows inserted (new records only).</summary>
    public int Inserted { get; init; }

    /// <summary>Number of records skipped (duplicate DeviceAddress + RecordedAt).</summary>
    public int Skipped { get; init; }

    /// <summary>Records that could not be inserted due to errors.</summary>
    public IReadOnlyList<BmsIngestFailure> Failed { get; init; } = [];
}

public class BmsIngestFailure
{
    public string DeviceAddress { get; init; } = string.Empty;
    public DateTime RecordedAtUtc { get; init; }
    public string Error { get; init; } = string.Empty;
}

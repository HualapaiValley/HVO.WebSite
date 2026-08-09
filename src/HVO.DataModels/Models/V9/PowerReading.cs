using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.DataModels.Models.V9;

/// <summary>
/// Unit-normalized power-system snapshot from an edge source such as SolarAssistant.
/// One row represents one source snapshot at one source timestamp.
/// Electrical signs remain source-native for legacy compatibility.
/// </summary>
[Table("PowerReading", Schema = "v9")]
public class PowerReading
{
    [Key]
    public long Id { get; set; }

    /// <summary>Stable source identifier, for example "solarassistant-total".</summary>
    [MaxLength(64)]
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Source system/provider, for example "solarassistant".</summary>
    [MaxLength(64)]
    public string? SourceSystem { get; set; }

    /// <summary>Optional source device or topic group, for example "inverter_1".</summary>
    [MaxLength(64)]
    public string? DeviceId { get; set; }

    /// <summary>UTC time this snapshot was observed at the source/gateway.</summary>
    public DateTime RecordedAt { get; set; }

    public double? PvPowerW { get; set; }
    public double? LoadPowerW { get; set; }

    /// <summary>Grid power in W. Negative values represent export where the source uses that convention.</summary>
    public double? GridPowerW { get; set; }

    /// <summary>Source-native battery power in W; use source provenance when interpreting its sign.</summary>
    public double? BatteryPowerW { get; set; }

    public double? SystemPowerW { get; set; }
    public double? BatteryStateOfChargePercent { get; set; }
    public double? BatteryVoltageV { get; set; }
    /// <summary>Source-native battery current in A; use source provenance when interpreting its sign.</summary>
    public double? BatteryCurrentA { get; set; }
    public double? BatteryCapacityKwh { get; set; }
    public double? GridVoltageV { get; set; }
    public double? GridFrequencyHz { get; set; }
    public double? OutputVoltageV { get; set; }
    public double? OutputFrequencyHz { get; set; }
    public double? LoadPercentage { get; set; }

    [MaxLength(100)]
    public string? InverterMode { get; set; }

    [MaxLength(100)]
    public string? OutputSourcePriority { get; set; }

    [MaxLength(100)]
    public string? ChargerSourcePriority { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

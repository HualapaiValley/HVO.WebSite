namespace HVO.WebSite.v9.Models;

using HVO.Edge.Contracts.PowerSystem;

/// <summary>Request body for ingesting a normalized power-system snapshot.</summary>
public class PowerReadingIngestRequest
{
    /// <summary>Stable source identifier, for example "solarassistant-total".</summary>
    public string? SourceId { get; init; }

    /// <summary>Source system/provider, for example "solarassistant".</summary>
    public string? SourceSystem { get; init; }

    /// <summary>Optional source device or topic group, for example "inverter_1".</summary>
    public string? DeviceId { get; init; }

    /// <summary>UTC time this snapshot was observed at the source/gateway.</summary>
    public DateTime RecordedAtUtc { get; init; }

    public double? PvPowerW { get; init; }

    public double? LoadPowerW { get; init; }

    public double? GridPowerW { get; init; }

    public double? BatteryPowerW { get; init; }

    public double? SystemPowerW { get; init; }

    public double? BatteryStateOfChargePercent { get; init; }

    public double? BatteryVoltageV { get; init; }

    public double? BatteryCurrentA { get; init; }

    public double? BatteryCapacityKwh { get; init; }

    public double? GridVoltageV { get; init; }

    public double? GridFrequencyHz { get; init; }

    public double? OutputVoltageV { get; init; }

    public double? OutputFrequencyHz { get; init; }

    public double? LoadPercentage { get; init; }

    public string? InverterMode { get; init; }

    public string? OutputSourcePriority { get; init; }

    public string? ChargerSourcePriority { get; init; }
}

/// <summary>Response body returned from the batch power ingest endpoint.</summary>
public class PowerReadingBatchResponse
{
    public int Inserted { get; init; }
    public int Skipped { get; init; }
    public IReadOnlyList<PowerReadingBatchFailure> Failed { get; init; } = [];
}

public class PowerReadingBatchFailure
{
    public string SourceId { get; init; } = string.Empty;
    public DateTime RecordedAtUtc { get; init; }
    public string Error { get; init; } = string.Empty;
}

/// <summary>A normalized power-system snapshot returned from the API.</summary>
public class PowerReadingResponse
{
    public long Id { get; init; }
    public string SourceId { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? DeviceId { get; init; }
    public DateTime RecordedAt { get; init; }
    public double? PvPowerW { get; init; }
    public double? LoadPowerW { get; init; }
    public double? GridPowerW { get; init; }
    public double? BatteryPowerW { get; init; }
    public double? SystemPowerW { get; init; }
    public double? BatteryStateOfChargePercent { get; init; }
    public double? BatteryVoltageV { get; init; }
    public double? BatteryCurrentA { get; init; }
    public double? BatteryCapacityKwh { get; init; }
    public double? GridVoltageV { get; init; }
    public double? GridFrequencyHz { get; init; }
    public double? OutputVoltageV { get; init; }
    public double? OutputFrequencyHz { get; init; }
    public double? LoadPercentage { get; init; }
    public string? InverterMode { get; init; }
    public string? OutputSourcePriority { get; init; }
    public string? ChargerSourcePriority { get; init; }
}

public class PowerSnapshotIngestResponse
{
    public bool Inserted { get; init; }
    public bool Skipped { get; init; }
    public string? Error { get; init; }
}

public class PowerDeviceInventorySnapshotResponse
{
    public string SourceId { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public bool IsPresent { get; init; }
    public bool IsStale { get; init; }
    public int RestMetricCount { get; init; }
    public int MqttEntityCount { get; init; }
    public int MqttStateTopicCount { get; init; }
    public IReadOnlyList<PowerDeviceInventoryDevice> Devices { get; init; } = [];
}

public class PowerConfigurationSnapshotResponse
{
    public string SourceId { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public bool IsPresent { get; init; }
    public bool IsStale { get; init; }
    public IReadOnlyList<PowerConfigurationSetting> Settings { get; init; } = [];
    public IReadOnlyList<PowerCommandCapability> CommandCapabilities { get; init; } = [];
}

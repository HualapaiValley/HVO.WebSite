namespace HVO.WebSite.v9.Models;

using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;

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

public class PowerEnergySnapshotResponse
{
    public string SourceId { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public bool IsPresent { get; init; }
    public bool IsStale { get; init; }
    public bool CounterResetDetected { get; init; }
    public IReadOnlyList<PowerEnergyCounter> Counters { get; init; } = [];
}

public class PowerInverterDetailSnapshotResponse
{
    public string SourceId { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public bool IsPresent { get; init; }
    public bool IsStale { get; init; }
    public IReadOnlyList<PowerPvStringDetail> PvStrings { get; init; } = [];
    public PowerInverterAcDetail? Ac { get; init; }
    public PowerInverterLoadDetail? Load { get; init; }
    public PowerInverterBatteryDetail? Battery { get; init; }
    public PowerInverterOperatingDetail? Operating { get; init; }
    public double? TemperatureC { get; init; }
    public IReadOnlyList<PowerInverterTemperatureDetail> Temperatures { get; init; } = [];
    public IReadOnlyList<PowerInverterStatusDetail> Statuses { get; init; } = [];
}

public class PowerMpptDetailSnapshotResponse
{
    public long Id { get; init; }
    public string SourceId { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public bool IsPresent { get; init; }
    public bool IsStale { get; init; }
    public IReadOnlyList<PowerMpptTrackerDetail> Trackers { get; init; } = [];
    public PowerMpptBatteryOutputDetail? BatteryOutput { get; init; }
    public IReadOnlyList<PowerMpptTemperatureDetail> Temperatures { get; init; } = [];
    public IReadOnlyList<PowerMpptDiagnosticDetail> Diagnostics { get; init; } = [];
}

public sealed record PowerBatteryHistoryPoint(
    DateTime RecordedAtUtc,
    string SourceId,
    string? DeviceId,
    double? PowerW);

public sealed record PowerTelemetryHistoryResponse(
    IReadOnlyList<PowerMpptDetailSnapshotResponse> MpptDetails,
    IReadOnlyList<PowerBatteryHistoryPoint> BatteryReadings)
{
    public static PowerTelemetryHistoryResponse Empty { get; } = new([], []);
}

public class GatewayStatusSnapshotResponse
{
    public string SourceId { get; init; } = string.Empty;
    public string? SourceSystem { get; init; }
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public bool IsPresent { get; init; }
    public bool IsStale { get; init; }
    public GatewayIdentity? Identity { get; init; }
    public GatewayHealthSnapshot? Health { get; init; }
    public GatewayRuntimeSignal? Rest { get; init; }
    public GatewayRuntimeSignal? Mqtt { get; init; }
    public GatewayOutboxStatus? Outbox { get; init; }
    public int? RestMetricCount { get; init; }
    public int? MqttEntityCount { get; init; }
    public int? MqttStateTopicCount { get; init; }
    public int? MqttCommandTopicCount { get; init; }
}

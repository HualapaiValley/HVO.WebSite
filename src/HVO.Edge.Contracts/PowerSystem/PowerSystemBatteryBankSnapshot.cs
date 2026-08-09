namespace HVO.Edge.Contracts.PowerSystem;

/// <summary>
/// Rich source-specific battery-bank data. Electrical signs remain source-native for
/// compatibility; use <see cref="PowerBatteryObservation"/> for canonical composed signs.
/// </summary>
public sealed record PowerSystemBatteryBankSnapshot(
    string BankId,
    DateTime RecordedAtUtc,
    PowerMetricSource Source,
    string? SourceId = null,
    string? DeviceId = null,
    SourcedValue<double>? StateOfChargePercent = null,
    SourcedValue<double>? VoltageV = null,
    SourcedValue<double>? CurrentA = null,
    SourcedValue<double>? PowerW = null,
    SourcedValue<double>? StateOfHealthPercent = null,
    SourcedValue<double>? MinCellVoltageV = null,
    SourcedValue<double>? MaxCellVoltageV = null,
    SourcedValue<double>? DeltaCellVoltageV = null,
    SourcedValue<double>? AverageCellVoltageV = null,
    SourcedValue<double>? BatteryTemperature1C = null,
    SourcedValue<double>? BatteryTemperature2C = null,
    SourcedValue<double>? PowerTubeTemperatureC = null,
    SourcedValue<bool>? BalancingActive = null,
    SourcedValue<double>? BalancingCurrentA = null,
    SourcedValue<bool>? HasAlarms = null);

namespace HVO.Edge.Contracts.PowerSystem;

/// <summary>
/// Preferred composed battery summary. Positive current and power represent discharge;
/// negative values represent charge.
/// </summary>
public sealed record PowerSystemBatterySnapshot(
    SourcedValue<double>? StateOfChargePercent = null,
    SourcedValue<double>? VoltageV = null,
    SourcedValue<double>? CurrentA = null,
    SourcedValue<double>? PowerW = null,
    SourcedValue<PowerFlowDirection>? FlowDirection = null,
    SourcedValue<double>? CapacityKwh = null,
    SourcedValue<int>? BankCount = null,
    SourcedValue<bool>? HasAlarms = null);

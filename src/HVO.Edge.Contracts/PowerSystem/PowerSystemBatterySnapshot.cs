namespace HVO.Edge.Contracts.PowerSystem;

public sealed record PowerSystemBatterySnapshot(
    SourcedValue<double>? StateOfChargePercent = null,
    SourcedValue<double>? VoltageV = null,
    SourcedValue<double>? CurrentA = null,
    SourcedValue<double>? PowerW = null,
    SourcedValue<PowerFlowDirection>? FlowDirection = null,
    SourcedValue<double>? CapacityKwh = null,
    SourcedValue<int>? BankCount = null,
    SourcedValue<bool>? HasAlarms = null);

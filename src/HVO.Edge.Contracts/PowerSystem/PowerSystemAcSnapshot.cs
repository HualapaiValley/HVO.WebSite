namespace HVO.Edge.Contracts.PowerSystem;

public sealed record PowerSystemAcSnapshot(
    SourcedValue<double>? LoadPowerW = null,
    SourcedValue<double>? GridPowerW = null,
    SourcedValue<PowerFlowDirection>? GridFlowDirection = null,
    SourcedValue<double>? GridVoltageV = null,
    SourcedValue<double>? GridFrequencyHz = null,
    SourcedValue<double>? OutputVoltageV = null,
    SourcedValue<double>? OutputFrequencyHz = null,
    SourcedValue<double>? LoadPercent = null,
    SourcedValue<string>? InverterMode = null,
    SourcedValue<string>? OutputSourcePriority = null,
    SourcedValue<string>? ChargerSourcePriority = null);

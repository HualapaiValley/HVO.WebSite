namespace HVO.Edge.Contracts.PowerSystem;

public sealed class PowerInverterDetailPayload
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public IReadOnlyList<PowerPvStringDetail> PvStrings { get; init; } = [];
    public PowerInverterAcDetail? Ac { get; init; }
    public PowerInverterLoadDetail? Load { get; init; }
    public PowerInverterBatteryDetail? Battery { get; init; }
    public PowerInverterOperatingDetail? Operating { get; init; }
    public double? TemperatureC { get; init; }
    public IReadOnlyList<PowerInverterTemperatureDetail> Temperatures { get; init; } = [];
    public IReadOnlyList<PowerInverterStatusDetail> Statuses { get; init; } = [];
}

public sealed class PowerPvStringDetail
{
    public string StringId { get; init; } = string.Empty;
    public double? PowerW { get; init; }
    public double? VoltageV { get; init; }
    public double? CurrentA { get; init; }
}

public sealed class PowerInverterLoadDetail
{
    public double? LoadPowerW { get; init; }
    public double? LoadApparentPowerVa { get; init; }
    public double? SystemAndLoadPowerW { get; init; }
}

public sealed class PowerInverterAcDetail
{
    public double? InputVoltageV { get; init; }
    public double? InputFrequencyHz { get; init; }
    public double? OutputVoltageV { get; init; }
    public double? OutputFrequencyHz { get; init; }
}

public sealed class PowerInverterBatteryDetail
{
    public double? VoltageV { get; init; }
    public double? CurrentA { get; init; }
    public double? PowerW { get; init; }
}

public sealed class PowerInverterOperatingDetail
{
    public string? Mode { get; init; }
    public string? FaultCode { get; init; }
    public double? LoadPercentage { get; init; }
    public string? StatusFlags { get; init; }
}

public sealed class PowerInverterTemperatureDetail
{
    public string TemperatureId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double? TemperatureC { get; init; }
}

public sealed class PowerInverterStatusDetail
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? SourceTopic { get; init; }
}

namespace HVO.Gateway.SolarAssistant.SolarAssistant;

public sealed class PowerSnapshotHistoryPoint
{
    public DateTime RecordedAtUtc { get; init; }

    public double? PvPowerW { get; init; }

    public double? LoadPowerW { get; init; }

    public double? GridPowerW { get; init; }

    public double? BatteryPowerW { get; init; }
}

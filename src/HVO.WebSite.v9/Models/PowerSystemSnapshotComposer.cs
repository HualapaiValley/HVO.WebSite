using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.WebSite.v9.Models;

public static class PowerSystemSnapshotComposer
{
    private const string SolarAssistantSystem = "solarassistant";
    private const string SmartShuntSystem = "victron-smartshunt";

    public static PowerSystemSnapshot Compose(IReadOnlyList<PowerReading> readings, DateTime observedAtUtc)
    {
        var latestSolarAssistant = LatestBySourceSystem(readings, SolarAssistantSystem);
        var latestSmartShunt = LatestBySourceSystem(readings, SmartShuntSystem);

        return new PowerSystemSnapshot(
            ObservedAtUtc: observedAtUtc,
            Ac: ComposeAc(latestSolarAssistant),
            Pv: ComposePv(latestSolarAssistant),
            Battery: ComposeBattery(latestSolarAssistant, latestSmartShunt));
    }

    private static PowerSystemAcSnapshot? ComposeAc(PowerReading? solarAssistant)
    {
        if (solarAssistant is null)
            return null;

        return new PowerSystemAcSnapshot(
            LoadPowerW: Sourced(solarAssistant.LoadPowerW, PowerMetricSource.SolarAssistant, solarAssistant),
            GridPowerW: Sourced(solarAssistant.GridPowerW, PowerMetricSource.SolarAssistant, solarAssistant),
            GridFlowDirection: Sourced(FlowFromSignedGridPower(solarAssistant.GridPowerW), PowerMetricSource.SolarAssistant, solarAssistant),
            GridVoltageV: Sourced(solarAssistant.GridVoltageV, PowerMetricSource.SolarAssistant, solarAssistant),
            GridFrequencyHz: Sourced(solarAssistant.GridFrequencyHz, PowerMetricSource.SolarAssistant, solarAssistant),
            OutputVoltageV: Sourced(solarAssistant.OutputVoltageV, PowerMetricSource.SolarAssistant, solarAssistant),
            OutputFrequencyHz: Sourced(solarAssistant.OutputFrequencyHz, PowerMetricSource.SolarAssistant, solarAssistant),
            LoadPercent: Sourced(solarAssistant.LoadPercentage, PowerMetricSource.SolarAssistant, solarAssistant),
            InverterMode: Sourced(solarAssistant.InverterMode, PowerMetricSource.SolarAssistant, solarAssistant),
            OutputSourcePriority: Sourced(solarAssistant.OutputSourcePriority, PowerMetricSource.SolarAssistant, solarAssistant),
            ChargerSourcePriority: Sourced(solarAssistant.ChargerSourcePriority, PowerMetricSource.SolarAssistant, solarAssistant));
    }

    private static PowerSystemPvSnapshot? ComposePv(PowerReading? solarAssistant)
    {
        if (solarAssistant?.PvPowerW is null)
            return null;

        return new PowerSystemPvSnapshot(
            PowerW: Sourced(solarAssistant.PvPowerW, PowerMetricSource.SolarAssistant, solarAssistant));
    }

    private static PowerSystemBatterySnapshot? ComposeBattery(PowerReading? solarAssistant, PowerReading? smartShunt)
    {
        if (solarAssistant is null && smartShunt is null)
            return null;

        var voltageSource = smartShunt?.BatteryVoltageV is not null ? smartShunt : solarAssistant;
        var currentSource = smartShunt?.BatteryCurrentA is not null ? smartShunt : solarAssistant;
        var powerSource = smartShunt?.BatteryPowerW is not null ? smartShunt : solarAssistant;
        var socSource = solarAssistant?.BatteryStateOfChargePercent is not null ? solarAssistant : smartShunt;
        var socConfidence = ReferenceEquals(socSource, smartShunt) ? "fallback-untrusted" : null;

        return new PowerSystemBatterySnapshot(
            StateOfChargePercent: Sourced(socSource?.BatteryStateOfChargePercent, SourceFor(socSource), socSource, socConfidence),
            VoltageV: Sourced(voltageSource?.BatteryVoltageV, SourceFor(voltageSource), voltageSource),
            CurrentA: Sourced(currentSource?.BatteryCurrentA, SourceFor(currentSource), currentSource),
            PowerW: Sourced(powerSource?.BatteryPowerW, SourceFor(powerSource), powerSource),
            FlowDirection: Sourced(FlowFromSignedBatteryPower(powerSource?.BatteryPowerW), SourceFor(powerSource), powerSource),
            CapacityKwh: Sourced(solarAssistant?.BatteryCapacityKwh, PowerMetricSource.SolarAssistant, solarAssistant));
    }

    private static PowerReading? LatestBySourceSystem(IReadOnlyList<PowerReading> readings, string sourceSystem)
        => readings
            .Where(reading => string.Equals(reading.SourceSystem, sourceSystem, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(reading => reading.RecordedAt)
            .ThenByDescending(reading => reading.Id)
            .FirstOrDefault();

    private static PowerMetricSource SourceFor(PowerReading? reading)
        => reading?.SourceSystem?.Trim().ToLowerInvariant() switch
        {
            SolarAssistantSystem => PowerMetricSource.SolarAssistant,
            SmartShuntSystem => PowerMetricSource.VictronSmartShunt,
            _ => PowerMetricSource.Unknown,
        };

    private static SourcedValue<T>? Sourced<T>(T? value, PowerMetricSource source, PowerReading? reading, string? confidence = null)
        where T : struct
        => value.HasValue && reading is not null
            ? new SourcedValue<T>(value.Value, source, reading.RecordedAt, reading.SourceId, reading.DeviceId, confidence)
            : null;

    private static SourcedValue<string>? Sourced(string? value, PowerMetricSource source, PowerReading? reading)
        => !string.IsNullOrWhiteSpace(value) && reading is not null
            ? new SourcedValue<string>(value, source, reading.RecordedAt, reading.SourceId, reading.DeviceId)
            : null;

    private static PowerFlowDirection? FlowFromSignedGridPower(double? gridPowerW)
        => gridPowerW switch
        {
            > 0 => PowerFlowDirection.Import,
            < 0 => PowerFlowDirection.Export,
            0 => PowerFlowDirection.Idle,
            _ => null,
        };

    private static PowerFlowDirection? FlowFromSignedBatteryPower(double? batteryPowerW)
        => batteryPowerW switch
        {
            > 0 => PowerFlowDirection.Discharging,
            < 0 => PowerFlowDirection.Charging,
            0 => PowerFlowDirection.Idle,
            _ => null,
        };
}

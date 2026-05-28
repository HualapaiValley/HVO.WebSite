using System.Globalization;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.WebSite.v9.Models;

public static class PowerSystemSnapshotComposer
{
    private const string SolarAssistantSystem = "solarassistant";
    private const string SmartShuntSystem = "victron-smartshunt";

    public static PowerSystemSnapshot Compose(
        IReadOnlyList<PowerReading> readings,
        DateTime observedAtUtc,
        IReadOnlyList<BmsReading>? bmsReadings = null)
    {
        var latestSolarAssistant = LatestBySourceSystem(readings, SolarAssistantSystem);
        var latestSmartShunt = LatestBySourceSystem(readings, SmartShuntSystem);
        var batteryBanks = ComposeBatteryBanks(bmsReadings ?? []);

        return new PowerSystemSnapshot(
            ObservedAtUtc: observedAtUtc,
            Ac: ComposeAc(latestSolarAssistant),
            Pv: ComposePv(latestSolarAssistant),
            Battery: ComposeBattery(latestSolarAssistant, latestSmartShunt, batteryBanks),
            BatteryBanks: batteryBanks.Count > 0 ? batteryBanks : null);
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

    private static PowerSystemBatterySnapshot? ComposeBattery(
        PowerReading? solarAssistant,
        PowerReading? smartShunt,
        IReadOnlyList<PowerSystemBatteryBankSnapshot> batteryBanks)
    {
        if (solarAssistant is null && smartShunt is null && batteryBanks.Count == 0)
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
            CapacityKwh: Sourced(solarAssistant?.BatteryCapacityKwh, PowerMetricSource.SolarAssistant, solarAssistant),
            BankCount: batteryBanks.Count > 0 ? Sourced(batteryBanks.Count, PowerMetricSource.JkBms, batteryBanks.Max(b => b.RecordedAtUtc)) : null,
            HasAlarms: batteryBanks.Count > 0 ? Sourced(batteryBanks.Any(b => b.HasAlarms?.Value == true), PowerMetricSource.JkBms, batteryBanks.Max(b => b.RecordedAtUtc)) : null);
    }

    private static IReadOnlyList<PowerSystemBatteryBankSnapshot> ComposeBatteryBanks(IReadOnlyList<BmsReading> bmsReadings)
        => bmsReadings
            .OrderBy(reading => reading.Device?.Alias ?? reading.Device?.Address ?? reading.DeviceId.ToString(CultureInfo.InvariantCulture))
            .Select(reading =>
            {
                var bankId = !string.IsNullOrWhiteSpace(reading.Device?.Alias)
                    ? reading.Device.Alias
                    : reading.Device?.Address ?? $"device-{reading.DeviceId}";
                var cellVoltages = reading.CellVoltages.Select(v => v.VoltageMv).ToArray();
                var minCellVoltageMv = cellVoltages.Length > 0 ? cellVoltages.Min() : (int?)null;
                var maxCellVoltageMv = cellVoltages.Length > 0 ? cellVoltages.Max() : (int?)null;
                var averageCellVoltageMv = cellVoltages.Length > 0 ? cellVoltages.Average() : (double?)null;

                return new PowerSystemBatteryBankSnapshot(
                    BankId: bankId,
                    RecordedAtUtc: reading.RecordedAt,
                    Source: PowerMetricSource.JkBms,
                    SourceId: reading.Device?.Address,
                    DeviceId: bankId,
                    StateOfChargePercent: Sourced((double)reading.SocPercent, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    VoltageV: Sourced(reading.PackVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    CurrentA: Sourced(reading.CurrentMa / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    PowerW: Sourced(reading.PowerWatts, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    StateOfHealthPercent: Sourced((double)reading.SohPercent, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    MinCellVoltageV: Sourced(minCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    MaxCellVoltageV: Sourced(maxCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    DeltaCellVoltageV: Sourced(reading.DeltaCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    AverageCellVoltageV: Sourced(averageCellVoltageMv / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BatteryTemperature1C: Sourced(reading.BatteryTemp1C, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BatteryTemperature2C: Sourced(reading.BatteryTemp2C, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    PowerTubeTemperatureC: Sourced(reading.PowerTubeC, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BalancingActive: Sourced(reading.BalancingActive, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    BalancingCurrentA: Sourced(reading.BalancingCurrentMa / 1000.0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId),
                    HasAlarms: Sourced(reading.AlarmBitmask != 0, PowerMetricSource.JkBms, reading.RecordedAt, reading.Device?.Address, bankId));
            })
            .ToArray();

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

    private static SourcedValue<T>? Sourced<T>(T? value, PowerMetricSource source, DateTime recordedAt, string? sourceId = null, string? deviceId = null)
        where T : struct
        => value.HasValue ? new SourcedValue<T>(value.Value, source, recordedAt, sourceId, deviceId) : null;

    private static SourcedValue<T> Sourced<T>(T value, PowerMetricSource source, DateTime recordedAt, string? sourceId = null, string? deviceId = null)
        where T : struct
        => new(value, source, recordedAt, sourceId, deviceId);

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

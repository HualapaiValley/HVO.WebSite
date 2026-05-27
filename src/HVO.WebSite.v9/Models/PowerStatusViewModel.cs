using System.Globalization;
using HVO.Edge.Contracts.PowerSystem;

namespace HVO.WebSite.v9.Models;

public sealed record PowerStatusViewModel(
    string PvPower,
    string LoadPower,
    string BatteryPower,
    string BatteryFlow,
    string BatteryStateOfCharge,
    string BatterySource,
    string GridPower,
    string GridFlow,
    string InverterMode,
    string BatteryBankCount,
    string BatteryAlarmState,
    IReadOnlyList<PowerStatusBankViewModel> BatteryBanks,
    string ObservedAt,
    string SnapshotState)
{
    public static PowerStatusViewModel Empty { get; } = new(
        PvPower: "--",
        LoadPower: "--",
        BatteryPower: "--",
        BatteryFlow: "Unknown",
        BatteryStateOfCharge: "--",
        BatterySource: "No source",
        GridPower: "--",
        GridFlow: "Unknown",
        InverterMode: "Unknown",
        BatteryBankCount: "--",
        BatteryAlarmState: "Unknown",
        BatteryBanks: [],
        ObservedAt: "Waiting for power telemetry",
        SnapshotState: "Waiting");

    public static PowerStatusViewModel FromSnapshot(PowerSystemSnapshot? snapshot)
    {
        if (snapshot is null)
            return Empty;

        var batteryPower = snapshot.Battery?.PowerW;
        var gridPower = snapshot.Ac?.GridPowerW;

        return new PowerStatusViewModel(
            PvPower: FormatWatts(snapshot.Pv?.PowerW?.Value),
            LoadPower: FormatWatts(snapshot.Ac?.LoadPowerW?.Value),
            BatteryPower: FormatSignedWatts(batteryPower?.Value),
            BatteryFlow: FormatFlow(snapshot.Battery?.FlowDirection?.Value),
            BatteryStateOfCharge: FormatPercent(snapshot.Battery?.StateOfChargePercent?.Value),
            BatterySource: FormatSource(snapshot.Battery?.PowerW?.Source ?? snapshot.Battery?.StateOfChargePercent?.Source),
            GridPower: FormatSignedWatts(gridPower?.Value),
            GridFlow: FormatFlow(snapshot.Ac?.GridFlowDirection?.Value),
            InverterMode: snapshot.Ac?.InverterMode?.Value ?? "Unknown",
            BatteryBankCount: FormatBankCount(snapshot.Battery?.BankCount?.Value),
            BatteryAlarmState: FormatAlarmState(snapshot.Battery?.HasAlarms?.Value),
            BatteryBanks: FormatBanks(snapshot.BatteryBanks),
            ObservedAt: $"Observed {snapshot.ObservedAtUtc.ToLocalTime().ToString("dd MMM yyyy - h:mm tt", CultureInfo.InvariantCulture)}",
            SnapshotState: "Live");
    }

    private static string FormatWatts(double? value)
        => value.HasValue ? $"{value.Value:0} W" : "--";

    private static string FormatSignedWatts(double? value)
        => value.HasValue ? $"{value.Value:+0;-0;0} W" : "--";

    private static string FormatPercent(double? value)
        => value.HasValue ? $"{value.Value:0}%" : "--";

    private static string FormatBankCount(int? value)
        => value switch
        {
            > 1 => $"{value.Value} banks",
            1 => "1 bank",
            _ => "--",
        };

    private static string FormatAlarmState(bool? hasAlarms)
        => hasAlarms switch
        {
            true => "Active alarm",
            false => "No alarms",
            _ => "Unknown",
        };

    private static IReadOnlyList<PowerStatusBankViewModel> FormatBanks(IReadOnlyList<PowerSystemBatteryBankSnapshot>? banks)
        => banks is { Count: > 0 }
            ? banks
                .OrderBy(b => b.BankId, StringComparer.OrdinalIgnoreCase)
                .Select(b => new PowerStatusBankViewModel(
                    BankId: b.BankId,
                    StateOfCharge: FormatPercent(b.StateOfChargePercent?.Value),
                    Voltage: FormatVolts(b.VoltageV?.Value),
                    Current: FormatSignedAmps(b.CurrentA?.Value),
                    DeltaCellVoltage: FormatMillivolts(b.DeltaCellVoltageV?.Value),
                    AlarmState: FormatAlarmState(b.HasAlarms?.Value),
                    IsAlarmed: b.HasAlarms?.Value == true))
                .ToArray()
            : [];

    private static string FormatVolts(double? value)
        => value.HasValue ? $"{value.Value:0.00} V" : "--";

    private static string FormatSignedAmps(double? value)
        => value.HasValue ? $"{value.Value:+0.0;-0.0;0.0} A" : "--";

    private static string FormatMillivolts(double? value)
        => value.HasValue ? $"{value.Value * 1000:0} mV" : "--";

    private static string FormatFlow(PowerFlowDirection? direction)
        => direction switch
        {
            PowerFlowDirection.Import => "Importing",
            PowerFlowDirection.Export => "Exporting",
            PowerFlowDirection.Charging => "Charging",
            PowerFlowDirection.Discharging => "Discharging",
            PowerFlowDirection.Idle => "Idle",
            _ => "Unknown",
        };

    private static string FormatSource(PowerMetricSource? source)
        => source switch
        {
            PowerMetricSource.SolarAssistant => "SolarAssistant",
            PowerMetricSource.JkBms => "JK BMS",
            PowerMetricSource.VictronSmartShunt => "SmartShunt",
            PowerMetricSource.Derived => "Derived",
            _ => "No source",
        };
}

public sealed record PowerStatusBankViewModel(
    string BankId,
    string StateOfCharge,
    string Voltage,
    string Current,
    string DeltaCellVoltage,
    string AlarmState,
    bool IsAlarmed);

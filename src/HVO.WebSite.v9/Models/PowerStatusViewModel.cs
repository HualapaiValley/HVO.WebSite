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
            ObservedAt: $"Observed {snapshot.ObservedAtUtc.ToLocalTime().ToString("dd MMM yyyy - h:mm tt", CultureInfo.InvariantCulture)}",
            SnapshotState: "Live");
    }

    private static string FormatWatts(double? value)
        => value.HasValue ? $"{value.Value:0} W" : "--";

    private static string FormatSignedWatts(double? value)
        => value.HasValue ? $"{value.Value:+0;-0;0} W" : "--";

    private static string FormatPercent(double? value)
        => value.HasValue ? $"{value.Value:0}%" : "--";

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

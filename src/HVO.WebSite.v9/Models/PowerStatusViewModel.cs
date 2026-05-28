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
    string BatteryFreshnessState,
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
        BatteryFreshnessState: "Unknown",
        BatteryBanks: [],
        ObservedAt: "Waiting for power telemetry",
        SnapshotState: "Waiting");

    public static PowerStatusViewModel FromSnapshot(PowerSystemSnapshot? snapshot)
    {
        if (snapshot is null)
            return Empty;

        var batteryPower = snapshot.Battery?.PowerW;
        var gridPower = snapshot.Ac?.GridPowerW;

        var batteryBanks = FormatBanks(snapshot.BatteryBanks, snapshot.ObservedAtUtc);

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
            BatteryFreshnessState: FormatBankFreshnessState(batteryBanks),
            BatteryBanks: batteryBanks,
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

    private static IReadOnlyList<PowerStatusBankViewModel> FormatBanks(IReadOnlyList<PowerSystemBatteryBankSnapshot>? banks, DateTime observedAtUtc)
        => banks is { Count: > 0 }
            ? banks
                .OrderBy(b => b.BankId, StringComparer.OrdinalIgnoreCase)
                .Select((b, index) => new PowerStatusBankViewModel(
                    BankId: b.BankId,
                    HeadingId: $"power-bank-{index + 1}",
                    Seen: FormatRelativeAge(b.RecordedAtUtc, observedAtUtc),
                    StateOfCharge: FormatPercent(b.StateOfChargePercent?.Value),
                    Voltage: FormatVolts(b.VoltageV?.Value),
                    Current: FormatSignedAmps(b.CurrentA?.Value),
                    DeltaCellVoltage: FormatMillivolts(b.DeltaCellVoltageV?.Value),
                    FreshnessStatus: FreshnessStatusFor(b.RecordedAtUtc, observedAtUtc),
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

    private static string FormatRelativeAge(DateTime recordedAtUtc, DateTime referenceUtc)
    {
        var age = referenceUtc - recordedAtUtc;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        var minutes = (int)age.TotalMinutes;
        if (minutes < 1)
            return "just now";

        if (minutes < 60)
            return $"{minutes} min ago";

        var hours = (int)age.TotalHours;
        return hours < 24
            ? $"{hours} hr ago"
            : $"{age.TotalDays:0.0} days ago";
    }

    private static string FreshnessStatusFor(DateTime recordedAtUtc, DateTime referenceUtc)
    {
        var age = referenceUtc - recordedAtUtc;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        return age.TotalMinutes switch
        {
            >= 15 => "stale",
            >= 5 => "warning",
            _ => "fresh",
        };
    }

    private static string FormatBankFreshnessState(IReadOnlyList<PowerStatusBankViewModel> banks)
    {
        if (banks.Count == 0)
            return "Unknown";

        var staleCount = banks.Count(b => b.FreshnessStatus == "stale");
        if (staleCount > 0)
            return staleCount == 1 ? "1 stale bank" : $"{staleCount} stale banks";

        var warningCount = banks.Count(b => b.FreshnessStatus == "warning");
        if (warningCount > 0)
            return warningCount == 1 ? "1 aging bank" : $"{warningCount} aging banks";

        return "All banks fresh";
    }

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
    string HeadingId,
    string Seen,
    string StateOfCharge,
    string Voltage,
    string Current,
    string DeltaCellVoltage,
    string FreshnessStatus,
    string AlarmState,
    bool IsAlarmed);

public sealed record PowerInventoryConfigurationViewModel(
    string State,
    string DeviceSummary,
    string ConfigurationSummary,
    string CommandCapabilitySummary,
    IReadOnlyList<string> Devices,
    IReadOnlyList<string> Settings)
{
    public static PowerInventoryConfigurationViewModel Empty { get; } = new(
        State: "Missing",
        DeviceSummary: "No device inventory received",
        ConfigurationSummary: "No configuration snapshot received",
        CommandCapabilitySummary: "No command capabilities inventoried",
        Devices: [],
        Settings: []);

    public static PowerInventoryConfigurationViewModel FromSnapshots(
        PowerDeviceInventorySnapshotResponse inventory,
        PowerConfigurationSnapshotResponse configuration)
    {
        var state = (inventory.IsPresent, inventory.IsStale, configuration.IsPresent, configuration.IsStale) switch
        {
            (true, false, true, false) => "Current",
            (false, _, false, _) => "Missing",
            (_, true, _, _) or (_, _, _, true) => "Stale",
            _ => "Partial",
        };

        var deviceSummary = inventory.IsPresent
            ? $"{inventory.Devices.Count} device(s), {inventory.RestMetricCount} REST metric(s), {inventory.MqttEntityCount} MQTT entit(ies)"
            : "No device inventory received";
        var configurationSummary = configuration.IsPresent
            ? $"{configuration.Settings.Count} read-only setting(s)"
            : "No configuration snapshot received";
        var commandSummary = configuration.IsPresent
            ? $"{configuration.CommandCapabilities.Count} command capabilit(ies) inventoried; writes disabled"
            : "No command capabilities inventoried";

        return new PowerInventoryConfigurationViewModel(
            State: state,
            DeviceSummary: deviceSummary,
            ConfigurationSummary: configurationSummary,
            CommandCapabilitySummary: commandSummary,
            Devices: inventory.Devices.Select(d => $"{d.Name} {d.Model}".Trim()).Where(d => d.Length > 0).Take(3).ToArray(),
            Settings: configuration.Settings.Select(s => s.Name).Where(s => !string.IsNullOrWhiteSpace(s)).Take(5).ToArray());
    }
}

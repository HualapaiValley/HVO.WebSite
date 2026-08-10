using System.Globalization;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.Themes.Components.Format;

namespace HVO.WebSite.v9.Models;

public sealed record PowerEg4EquipmentViewModel(
    string State,
    string InverterPvSubtotal,
    IReadOnlyList<PowerPvStringViewModel> InverterTrackers,
    string AcInput,
    string AcOutput,
    string Load,
    string InverterBattery,
    string OperatingState,
    IReadOnlyList<string> InverterTemperatures,
    IReadOnlyList<string> InverterStatuses,
    string ControllerPvSubtotal,
    IReadOnlyList<PowerPvStringViewModel> ControllerTrackers,
    string ControllerBatteryOutput,
    IReadOnlyList<string> ControllerTemperatures,
    IReadOnlyList<string> ControllerDiagnostics)
{
    public static PowerEg4EquipmentViewModel Empty { get; } = new(
        "Missing", "--", [], "--", "--", "--", "--", "Unknown", [], [], "--", [], "--", [], []);

    public static PowerEg4EquipmentViewModel FromSnapshots(
        PowerInverterDetailSnapshotResponse inverter,
        PowerMpptDetailSnapshotResponse controller)
    {
        if (!inverter.IsPresent && !controller.IsPresent)
            return Empty;

        var inverterTrackers = inverter.PvStrings
            .OrderBy(tracker => tracker.StringId, StringComparer.OrdinalIgnoreCase)
            .Select(tracker => Tracker(tracker.StringId, tracker.PowerW, tracker.VoltageV, tracker.CurrentA))
            .ToArray();
        var controllerTrackers = controller.Trackers
            .OrderBy(tracker => tracker.TrackerId, StringComparer.OrdinalIgnoreCase)
            .Select(tracker => Tracker(tracker.Name, tracker.PowerW, tracker.VoltageV, tracker.CurrentA))
            .ToArray();
        var state = !inverter.IsPresent || !controller.IsPresent
            ? "Partial"
            : inverter.IsStale || controller.IsStale ? "Stale" : "Current";

        return new PowerEg4EquipmentViewModel(
            State: state,
            InverterPvSubtotal: SumPower(inverter.PvStrings.Select(tracker => tracker.PowerW)),
            InverterTrackers: inverterTrackers,
            AcInput: FormatAc(inverter.Ac?.InputVoltageV, inverter.Ac?.InputFrequencyHz),
            AcOutput: FormatAc(inverter.Ac?.OutputVoltageV, inverter.Ac?.OutputFrequencyHz),
            Load: inverter.Load is null
                ? "--"
                : $"{HvoFormat.Power(inverter.Load.LoadPowerW)} active / {FormatVa(inverter.Load.LoadApparentPowerVa)} apparent",
            InverterBattery: FormatBattery(inverter.Battery?.VoltageV, inverter.Battery?.CurrentA, inverter.Battery?.PowerW),
            OperatingState: FormatOperating(inverter.Operating),
            InverterTemperatures: inverter.Temperatures.Select(temperature => $"{temperature.Name} {HvoFormat.Temperature(temperature.TemperatureC)}").ToArray(),
            InverterStatuses: inverter.Statuses.Select(status => $"{status.Key}: {status.Value}").Take(12).ToArray(),
            ControllerPvSubtotal: SumPower(controller.Trackers.Select(tracker => tracker.PowerW)),
            ControllerTrackers: controllerTrackers,
            ControllerBatteryOutput: FormatBattery(
                controller.BatteryOutput?.VoltageV,
                controller.BatteryOutput?.CurrentA,
                controller.BatteryOutput?.PowerW),
            ControllerTemperatures: controller.Temperatures.Select(temperature => $"{temperature.Name} {HvoFormat.Temperature(temperature.TemperatureC)}").ToArray(),
            ControllerDiagnostics: controller.Diagnostics.Select(diagnostic => $"{diagnostic.Name}: {diagnostic.Value}").Take(12).ToArray());
    }

    private static PowerPvStringViewModel Tracker(string id, double? powerW, double? voltageV, double? currentA) => new(
        id,
        HvoFormat.Power(powerW),
        HvoFormat.Voltage(voltageV),
        HvoFormat.Current(currentA));

    private static string SumPower(IEnumerable<double?> values)
    {
        var data = values.ToArray();
        return data.Length > 0 && data.All(value => value.HasValue)
            ? HvoFormat.Power(data.Sum(value => value!.Value))
            : "--";
    }

    private static string FormatBattery(double? voltageV, double? canonicalCurrentA, double? canonicalPowerW) =>
        $"{HvoFormat.Voltage(voltageV)}, {BatteryFacingCurrent(canonicalCurrentA)}, {BatteryFacingPower(canonicalPowerW)} (+ charging)";

    private static string BatteryFacingCurrent(double? canonicalCurrentA) =>
        HvoFormat.SignedCurrent(canonicalCurrentA.HasValue ? -canonicalCurrentA.Value : null);

    private static string BatteryFacingPower(double? canonicalPowerW) =>
        HvoFormat.SignedPower(canonicalPowerW.HasValue ? -canonicalPowerW.Value : null);

    private static string FormatAc(double? voltageV, double? frequencyHz)
    {
        if (!voltageV.HasValue && !frequencyHz.HasValue)
            return "--";
        var frequency = frequencyHz.HasValue
            ? $"{frequencyHz.Value.ToString("F1", CultureInfo.InvariantCulture)} Hz"
            : "--";
        return $"{HvoFormat.Voltage(voltageV, 1)} / {frequency}";
    }

    private static string FormatVa(double? value) => value.HasValue
        ? $"{value.Value.ToString("F0", CultureInfo.InvariantCulture)} VA"
        : "--";

    private static string FormatOperating(PowerInverterOperatingDetail? operating) => operating is null
        ? "Unknown"
        : $"Mode {operating.Mode ?? "--"}; load {HvoFormat.Percent(operating.LoadPercentage)}; fault {operating.FaultCode ?? "--"}";
}

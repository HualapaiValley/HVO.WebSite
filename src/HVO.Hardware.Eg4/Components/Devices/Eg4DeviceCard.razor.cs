using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace HVO.Hardware.Eg4.Components.Devices;

public partial class Eg4DeviceCard
{
    [Parameter, EditorRequired] public Eg4DashboardDevice Device { get; set; } = default!;
    private string TypeLabel => Device.Type == Eg4DeviceType.Inverter6500Ex ? "6500EX inverter" : "MPPT100-48HV controller";
    private string RoleLabel => Device.Role == PowerMeasurementRole.InverterBranch ? "Inverter branch" : "Charge-controller branch";
    private string StateLabel => Device.State switch
    {
        Eg4DashboardDeviceState.Disabled => "Disabled",
        Eg4DashboardDeviceState.Unavailable => "Telemetry unavailable",
        _ => Device.State.ToString(),
    };
    private string StateChipClass => Device.State switch
    {
        Eg4DashboardDeviceState.Online => "hvo-chip-success",
        Eg4DashboardDeviceState.Waiting => "hvo-chip-warning",
        Eg4DashboardDeviceState.Degraded => "hvo-chip-warning",
        Eg4DashboardDeviceState.Offline => "hvo-chip-danger",
        Eg4DashboardDeviceState.Unavailable => "hvo-chip-warning",
        _ => string.Empty,
    };
    private string PowerState => Device.CurrentA switch
    {
        > 0 => "Discharging",
        < 0 => "Charging",
        0 => "Idle",
        _ => "Unavailable",
    };
    private string BatteryCurrent => BatteryFacingCurrent(Device.CurrentA);
    private string BatteryPower => BatteryFacingPower(Device.PowerW);
    private string PvHeading => Device.Type == Eg4DeviceType.Inverter6500Ex ? "Inverter PV subtotal" : "Controller PV";
    private double? PvSubtotalW
    {
        get
        {
            var trackers = Device.MpptDetail?.Trackers;
            return trackers is { Count: > 0 } && trackers.All(tracker => tracker.PowerW.HasValue)
                ? trackers.Sum(tracker => tracker.PowerW!.Value)
                : null;
        }
    }
    private string AcInput => FormatAc(Device.InverterDetail?.Ac?.InputVoltageV, Device.InverterDetail?.Ac?.InputFrequencyHz);
    private string AcOutput => FormatAc(Device.InverterDetail?.Ac?.OutputVoltageV, Device.InverterDetail?.Ac?.OutputFrequencyHz);
    private string LoadPower => HvoFormat.Power(Device.InverterDetail?.Load?.LoadPowerW);
    private string ApparentLoad => Device.InverterDetail?.Load?.LoadApparentPowerVa is { } value
        ? $"{value.ToString("F0", CultureInfo.InvariantCulture)} VA"
        : "--";
    private string FreshnessLabel => Device.ObservedAtUtc is null ? "Never observed" : Device.IsStale ? "Stale" : "Fresh";
    private string ProvenanceLabel => string.Equals(Device.Confidence, "simulated", StringComparison.OrdinalIgnoreCase)
        ? "Simulated"
        : Device.Provenance switch
    {
        PowerObservationProvenance.Direct => "Direct / reported",
        PowerObservationProvenance.Derived => "Derived",
        PowerObservationProvenance.SourceAggregate => "Source aggregate",
        _ => "Unavailable",
    };

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

    private static string FormatTemperature(PowerInverterTemperatureDetail temperature) =>
        $"{temperature.Name} {HvoFormat.Temperature(temperature.TemperatureC)}";

    private static string TrackerProvenance(PowerMpptTrackerDetail tracker) => tracker.Provenance switch
    {
        PowerObservationProvenance.Direct => "Direct",
        PowerObservationProvenance.Derived => "Derived",
        _ => "Reported",
    };
}

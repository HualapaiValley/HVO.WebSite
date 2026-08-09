using HVO.Edge.Contracts.PowerSystem;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Dashboard;
using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.Eg4.Components.Devices;

public partial class Eg4DeviceCard
{
    [Parameter, EditorRequired] public Eg4DashboardDevice Device { get; set; } = default!;
    private string TypeLabel => Device.Type == Eg4DeviceType.Inverter6500Ex ? "6500EX inverter" : "MPPT100-48HV controller";
    private string RoleLabel => Device.Role == PowerMeasurementRole.InverterBranch ? "Inverter branch" : "Charge-controller branch";
    private string StateLabel => Device.State == Eg4DashboardDeviceState.Disabled ? "Disabled" : Device.State.ToString();
    private string StateChipClass => Device.State switch
    {
        Eg4DashboardDeviceState.Online => "hvo-chip-success",
        Eg4DashboardDeviceState.Waiting => "hvo-chip-warning",
        Eg4DashboardDeviceState.Degraded => "hvo-chip-warning",
        Eg4DashboardDeviceState.Offline => "hvo-chip-danger",
        _ => string.Empty,
    };
    private string PowerState => Device.CurrentA switch
    {
        > 0 => "Discharging",
        < 0 => "Charging",
        0 => "Idle",
        _ => "Unavailable",
    };
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
}

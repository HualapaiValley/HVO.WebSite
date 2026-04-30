using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class DeviceDetail : IDisposable
{
    [Parameter] public string Address { get; set; } = string.Empty;

    private Workers.DevicePollState? _state;

    protected override void OnParametersSet()
    {
        _state = Poller.DeviceStates
            .FirstOrDefault(d => string.Equals(d.Address, Address, StringComparison.OrdinalIgnoreCase));
    }

    protected override void OnInitialized()
    {
        Poller.DeviceStateChanged += OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Poller.DeviceStateChanged -= OnStateChanged;
    }

    /// <summary>Decodes the alarm bitmask into human-readable alarm names.</summary>
    private static IEnumerable<string> DecodeAlarms(uint bitmask)
    {
        // Bit definitions from esphome-jk-bms (JK02_24S variant)
        if ((bitmask & (1u <<  0)) != 0) yield return "Charge Overtemperature";
        if ((bitmask & (1u <<  1)) != 0) yield return "Charge Undertemperature";
        if ((bitmask & (1u <<  2)) != 0) yield return "Cell Overvoltage (cell)";
        if ((bitmask & (1u <<  3)) != 0) yield return "Cell Undervoltage";
        if ((bitmask & (1u <<  4)) != 0) yield return "Pack Undervoltage";
        if ((bitmask & (1u <<  5)) != 0) yield return "Discharge Overcurrent";
        if ((bitmask & (1u <<  6)) != 0) yield return "Charge Overcurrent";
        if ((bitmask & (1u <<  7)) != 0) yield return "Discharge Overtemperature";
        if ((bitmask & (1u <<  8)) != 0) yield return "Short Circuit";
        if ((bitmask & (1u <<  9)) != 0) yield return "Discharge Undertemperature";
        if ((bitmask & (1u << 10)) != 0) yield return "Charge Overcurrent (protection)";
        if ((bitmask & (1u << 11)) != 0) yield return "Cell Overvoltage (protection)";
        if ((bitmask & (1u << 12)) != 0) yield return "Cell Overvoltage";
        if ((bitmask & (1u << 13)) != 0) yield return "Pack Overvoltage";
        if ((bitmask & (1u << 14)) != 0) yield return "Low Capacity";
        if ((bitmask & (1u << 15)) != 0) yield return "MOS Overtemperature";

        // Bits 16–31: reserved — report raw position for unknown flags
        for (int bit = 16; bit < 32; bit++)
            if ((bitmask & (1u << bit)) != 0)
                yield return $"Unknown flag (bit {bit})";
    }
}

using HVO.Hardware.JkBms.Components.Layout;
using HVO.Hardware.JkBms.Configuration;
using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Devices : IDisposable
{
    [CascadingParameter] private ShellLayoutState? ShellLayoutState { get; set; }

    private IReadOnlyList<BmsDeviceConfig> ConfiguredDevices => Options.Value.Devices
        .Where(device => !string.IsNullOrWhiteSpace(device.Address) && !string.IsNullOrWhiteSpace(device.Alias))
        .ToList();

    protected override void OnInitialized()
    {
        Poller.DeviceStateChanged += OnStateChanged;
    }

    protected override void OnParametersSet()
    {
        ShellLayoutState?.SetPage("Banks", "Configured JK BMS banks", "Per-bank inventory and polling configuration for the active JK BMS fleet.");
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Poller.DeviceStateChanged -= OnStateChanged;
    }
}

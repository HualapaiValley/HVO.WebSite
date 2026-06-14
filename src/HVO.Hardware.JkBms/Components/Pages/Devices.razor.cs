using HVO.Hardware.JkBms.Configuration;
using Microsoft.AspNetCore.Components;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Devices : IDisposable
{
    private IReadOnlyList<BmsDeviceConfig> ConfiguredDevices => Options.Value.Devices
        .Where(device => !string.IsNullOrWhiteSpace(device.Address) && !string.IsNullOrWhiteSpace(device.Alias))
        .ToList();

    protected override void OnInitialized()
    {
        Poller.DeviceStateChanged += OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Poller.DeviceStateChanged -= OnStateChanged;
    }
}

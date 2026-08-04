using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Workers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class DeviceAdmin : IDisposable
{
    [Parameter] public string Address { get; set; } = string.Empty;
    [Inject] private BmsPollerWorker Poller { get; set; } = default!;
    [Inject] private IOptions<JkBmsOptions> Options { get; set; } = default!;

    private BmsDeviceConfig? Config => Options.Value.Devices.FirstOrDefault(device =>
        string.Equals(device.Address, Address, StringComparison.OrdinalIgnoreCase));

    private DevicePollState? State => Poller.DeviceStates.FirstOrDefault(device =>
        string.Equals(device.Address, Address, StringComparison.OrdinalIgnoreCase));

    private int EffectivePollIntervalSeconds => Config?.PollIntervalSeconds > 0
        ? Config.PollIntervalSeconds
        : Options.Value.DefaultPollIntervalSeconds;

    protected override void OnInitialized() => Poller.DeviceStateChanged += OnStateChanged;

    public void Dispose() => Poller.DeviceStateChanged -= OnStateChanged;

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);
}
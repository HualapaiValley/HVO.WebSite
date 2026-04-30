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
    private static IEnumerable<string> DecodeAlarms(uint bitmask) => AlarmDecoder.Decode(bitmask);
}

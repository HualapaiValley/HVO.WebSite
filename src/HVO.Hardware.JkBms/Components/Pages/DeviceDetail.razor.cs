using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class DeviceDetail : IDisposable
{
    [Parameter] public string Address { get; set; } = string.Empty;
    [Inject] private ILogger<DeviceDetail> Logger { get; set; } = default!;

    private Workers.DevicePollState? _state;

    protected override void OnParametersSet()
    {
        _state = Poller.DeviceStates
            .FirstOrDefault(d => string.Equals(d.Address, Address, StringComparison.OrdinalIgnoreCase));
        if (_state is null)
            Logger.LogWarning("Device detail requested for unknown address: {Address}", Address);
        else
            Logger.LogDebug("Device detail loaded for {Alias} ({Address})", _state.Alias, Address);
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

    private static string BuildGaugeStyle(double value, double min, double max, string color)
    {
        var normalized = max > min ? Math.Clamp((value - min) / (max - min) * 100d, 0d, 100d) : 0d;
        return $"--gauge-value:{normalized:0.##}; --gauge-color:{color};";
    }

    /// <summary>Decodes the alarm bitmask into human-readable alarm names.</summary>
    private static IEnumerable<string> DecodeAlarms(uint bitmask) => AlarmDecoder.Decode(bitmask);
}

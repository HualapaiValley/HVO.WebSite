using HVO.WebSite.Themes.Components.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class DeviceDetail : IDisposable
{
    [Parameter] public string Address { get; set; } = string.Empty;
    [Inject] private ILogger<DeviceDetail> Logger { get; set; } = default!;

    private Workers.DevicePollState? _state;
    private HvoChart? _cellChart;

    private string[] _cellLabels
        => _state?.LatestReading?.CellVoltagesMv
            .Select((_, i) => $"C{i + 1}")
            .ToArray() ?? [];

    private List<HvoChartDataset> _cellDatasets
    {
        get
        {
            var readings = _state?.LatestReading;
            if (readings is null) return new();
            var values = readings.CellVoltagesMv
                .Select(mv => mv / 1000.0).ToArray();
            return new()
            {
                new HvoChartDataset("Voltage", values,
                    BorderColor: "#6da5ff", BackgroundColor: "rgba(109,165,255,0.25)", BorderWidth: 1)
            };
        }
    }

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

    private async void OnStateChanged()
    {
        await InvokeAsync(StateHasChanged);
        if (_cellChart is not null)
            await _cellChart.RefreshAsync();
    }

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

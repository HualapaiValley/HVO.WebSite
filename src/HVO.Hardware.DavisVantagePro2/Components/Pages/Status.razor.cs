using HVO.Hardware.DavisVantagePro2.Protocol.Packets;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Status : IDisposable
{
    private Loop2Packet? _reading;
    private System.Threading.Timer? _timer;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            _timer = new System.Threading.Timer(_ => InvokeAsync(Refresh), null, 0, 10_000);
    }

    private void Refresh()
    {
        _reading = Worker.LatestReading;
        StateHasChanged();
    }

    public void Dispose() => _timer?.Dispose();

    private static string? F(double? v) => v?.ToString("F1");
    private static string? F0(double? v) => v?.ToString("F0");
}

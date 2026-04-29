using HVO.Hardware.DavisVantagePro2.Protocol.Packets;

namespace HVO.Hardware.DavisVantagePro2.Components.Pages;

public partial class Status : IDisposable
{
    private Loop2Packet? _reading;

    protected override void OnInitialized()
    {
        _reading = Worker.LatestReading;
        Worker.ReadingUpdated += OnReadingUpdated;
        Worker.WorkerStateChanged += OnStateChanged;
        Forwarder.SweptCompleted += OnStateChanged;
    }

    private void OnReadingUpdated(Loop2Packet reading)
    {
        _reading = reading;
        InvokeAsync(StateHasChanged);
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Worker.ReadingUpdated -= OnReadingUpdated;
        Worker.WorkerStateChanged -= OnStateChanged;
        Forwarder.SweptCompleted -= OnStateChanged;
    }

    private string? ToConsoleTime(DateTime? utc) =>
        utc.HasValue
            ? new DateTimeOffset(utc.Value, TimeSpan.Zero)
                  .ToOffset(Station.ConsoleUtcOffset)
                  .ToString("HH:mm:ss")
            : null;

    private static string? F(double? v) => v?.ToString("F1");
    private static string? F0(double? v) => v?.ToString("F0");
}

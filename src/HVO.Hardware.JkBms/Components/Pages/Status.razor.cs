namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Status : IDisposable
{
    protected override void OnInitialized()
    {
        Poller.DeviceStateChanged += OnStateChanged;
        Forwarder.SweepCompleted += OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Poller.DeviceStateChanged -= OnStateChanged;
        Forwarder.SweepCompleted -= OnStateChanged;
    }

    private static string F1(double v) => v.ToString("F1");
}

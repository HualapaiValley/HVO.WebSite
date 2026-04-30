namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Devices : IDisposable
{
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

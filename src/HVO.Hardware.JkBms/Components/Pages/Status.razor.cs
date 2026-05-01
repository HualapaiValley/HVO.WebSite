using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Status : IDisposable
{
    [Inject] private ILogger<Status> Logger { get; set; } = default!;

    protected override void OnInitialized()
    {
        Poller.DeviceStateChanged += OnStateChanged;
        Forwarder.SweepCompleted += OnStateChanged;
        Logger.LogInformation(
            "BMS status page loaded. {DeviceCount} device(s). Pending outbox: {Pending}",
            Poller.DeviceStates.Count, Forwarder.PendingCount);
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Poller.DeviceStateChanged -= OnStateChanged;
        Forwarder.SweepCompleted -= OnStateChanged;
    }

    private static string F1(double v) => v.ToString("F1");
}

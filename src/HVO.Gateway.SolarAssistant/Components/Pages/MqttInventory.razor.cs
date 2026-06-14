using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.WebSite.Themes.Components.Format;

namespace HVO.Gateway.SolarAssistant.Components.Pages;

public partial class MqttInventory : IDisposable
{
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    private SolarAssistantMqttInventory Inventory => MqttWorker.Inventory;

    protected override void OnInitialized()
    {
        _refreshCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    public void Dispose()
    {
        _refreshCts?.Cancel();
        _refreshTimer?.Dispose();
        _refreshCts?.Dispose();
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        if (_refreshTimer is null)
            return;

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(ct))
                await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string FormatTimestamp(DateTime? value) => HvoFormat.Timestamp(value, "MMM d, HH:mm:ss");

    private static string FormatCounts(IReadOnlyDictionary<string, int> counts) => counts.Count == 0
        ? "--"
        : string.Join(", ", counts.Take(6).Select(kvp => $"{kvp.Key}: {kvp.Value}"));

    private static string FormatDevices(IReadOnlyList<SolarAssistantMqttDeviceSummary> devices) => devices.Count == 0
        ? "--"
        : string.Join(", ", devices.Take(4).Select(d => d.Name));
}

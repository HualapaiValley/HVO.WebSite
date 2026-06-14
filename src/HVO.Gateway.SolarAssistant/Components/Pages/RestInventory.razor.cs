using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.WebSite.Themes.Components.Format;

namespace HVO.Gateway.SolarAssistant.Components.Pages;

public partial class RestInventory : IDisposable
{
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    private SolarAssistantMetricInventory? Inventory => SnapshotWorker.LastInventory;
    private string MetricCountText => Inventory is null ? "--" : Inventory.Topics.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

    private int ClassificationCount(string classification) => Inventory?.ClassificationCounts.TryGetValue(classification, out var count) == true ? count : 0;

    private static string FormatTimestamp(DateTime? value) => HvoFormat.Timestamp(value, "MMM d, HH:mm:ss");

    private static string FormatCounts(IReadOnlyDictionary<string, int>? counts) => counts is null || counts.Count == 0
        ? "--"
        : string.Join(", ", counts.Take(6).Select(kvp => $"{kvp.Key}: {kvp.Value}"));

    private static string FormatTopicMetadata(SolarAssistantMetricSummary topic)
    {
        var parts = new[] { topic.Group, topic.Name, topic.Unit }.Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" - ", parts.DefaultIfEmpty("No metadata"));
    }
}

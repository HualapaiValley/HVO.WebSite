using System.Net.Http.Json;
using HVO.Edge.Outbox;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Workers;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace HVO.Gateway.SolarAssistant.Components.Pages;

public partial class Status : IDisposable
{
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    [Inject] private SolarAssistantSnapshotWorker SnapshotWorker { get; set; } = default!;

    [Inject] private SolarAssistantMqttDiscoveryWorker MqttWorker { get; set; } = default!;

    [Inject] private PowerApiForwarder Forwarder { get; set; } = default!;

    [Inject] private SolarAssistantGatewayHealthService HealthService { get; set; } = default!;

    [Inject] private IOptions<SolarAssistantOptions> SolarAssistantOptionsAccessor { get; set; } = default!;

    [Inject] private IOptions<OutboxOptions> OutboxOptionsAccessor { get; set; } = default!;

    [Inject] private IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Inject] private ILogger<Status> Logger { get; set; } = default!;

    private SolarAssistantOptions SolarOptions => SolarAssistantOptionsAccessor.Value;
    private OutboxOptions OutboxOptions => OutboxOptionsAccessor.Value;
    private PowerReadingPayload? LatestSnapshot => SnapshotWorker.LastSnapshot;
    private SolarAssistantMetricInventory? Inventory => SnapshotWorker.LastInventory;
    private SolarAssistantMqttInventory MqttInventory => MqttWorker.Inventory;
    private SolarAssistantGatewayHealthSnapshot Health => HealthService.GetSnapshot();
    private IReadOnlyList<PowerSnapshotHistoryPoint> History => SnapshotWorker.History;
    private IReadOnlyList<SolarAssistantMetricSummary> InventoryTopics => Inventory?.Topics.Take(24).ToArray() ?? [];
    private IReadOnlyList<SolarAssistantMqttEntitySummary> MqttEntities => MqttInventory.Entities.Take(24).ToArray();

    private string SnapshotStatusText => SnapshotWorker.LastError is null && SnapshotWorker.LastSnapshotAt is not null
        ? "REST polling"
        : string.IsNullOrWhiteSpace(SolarOptions.Host) ? "REST disabled" : "REST issue";

    private Color SnapshotChipColor => SnapshotWorker.LastError is null && SnapshotWorker.LastSnapshotAt is not null
        ? Color.Success
        : Color.Warning;

    private string ForwarderStatusText => Forwarder.FailedCount > 0 || !string.IsNullOrWhiteSpace(Forwarder.LastError)
        ? "Forwarding issue"
        : Forwarder.PendingCount > 0 ? "Queued" : "Forwarding";

    private Color ForwarderChipColor => Forwarder.FailedCount > 0 || !string.IsNullOrWhiteSpace(Forwarder.LastError)
        ? Color.Error
        : Forwarder.PendingCount > 0 ? Color.Warning : Color.Success;

    private string MqttStatusText => MqttInventory.ConnectionState switch
    {
        "connected" => "MQTT connected",
        "connecting" => "MQTT connecting",
        "disabled" => "MQTT disabled",
        _ => "MQTT issue",
    };

    private Color MqttChipColor => MqttInventory.ConnectionState switch
    {
        "connected" => Color.Success,
        "connecting" => Color.Warning,
        "disabled" => Color.Warning,
        _ => Color.Error,
    };

    private string HealthStatusText => Health.State switch
    {
        "healthy" => "Gateway healthy",
        "warning" => $"{Health.Alerts.Count} warning(s)",
        "critical" => $"{Health.Alerts.Count} critical alert(s)",
        _ => "Gateway unknown",
    };

    private Color HealthChipColor => Health.State switch
    {
        "healthy" => Color.Success,
        "warning" => Color.Warning,
        "critical" => Color.Error,
        _ => Color.Default,
    };

    private string BatteryLevelCss => Math.Clamp(LatestSnapshot?.BatteryStateOfChargePercent ?? 0, 0, 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    private string DisplayHost => string.IsNullOrWhiteSpace(SolarOptions.Host) ? "Not configured" : SolarOptions.Host;
    private string EndpointSummary => Uri.TryCreate(OutboxOptions.ApiEndpoint, UriKind.Absolute, out var uri) ? uri.Host : "Not configured";
    private string InventorySummary => Inventory is null ? "Pending" : $"{Inventory.Topics.Count} topics";
    private string MqttSummary => MqttInventory.EntityCount > 0
        ? $"{MqttInventory.EntityCount} entities"
        : MqttInventory.StateTopicCount > 0 ? $"{MqttInventory.StateTopicCount} states" : MqttInventory.ConnectionState;

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
    private static string FormatWatts(double? value) => HvoFormat.Power(value);
    private static string FormatPercent(double? value) => HvoFormat.Percent(value);
    private static string FormatVolts(double? value) => HvoFormat.Voltage(value, 1);
    private static string FormatAmps(double? value) => HvoFormat.Current(value);
    private static string FormatKwh(double? value) => value.HasValue ? $"{value.Value:0.0} kWh" : "--";

    private static string FormatCounts(IReadOnlyDictionary<string, int>? counts) => counts is null || counts.Count == 0
        ? "--"
        : string.Join(", ", counts.Take(4).Select(kvp => $"{kvp.Key}: {kvp.Value}"));

    private static string FormatDevices(IReadOnlyList<SolarAssistantMqttDeviceSummary> devices) => devices.Count == 0
        ? "--"
        : string.Join(", ", devices.Take(3).Select(d => d.Name));

    private static Severity AlertSeverity(SolarAssistantGatewayHealthSeverity severity) => severity switch
    {
        SolarAssistantGatewayHealthSeverity.Critical => Severity.Error,
        SolarAssistantGatewayHealthSeverity.Warning => Severity.Warning,
        _ => Severity.Info,
    };

    private int _outboxBatchSize = 50;
    private int _outboxSweepIntervalSeconds = 5;
    private bool _outboxDirty;
    private bool _outboxOverride;
    private string? _outboxStatusMessage;

    private async Task SaveOutboxSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", OutboxOptionsAccessor.Value.ApiKey);
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { batchSize = _outboxBatchSize, sweepIntervalSeconds = _outboxSweepIntervalSeconds });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OutboxSettingsResponse>();
                _outboxOverride = result?.IsOverride ?? false;
                _outboxStatusMessage = _outboxOverride
                    ? $"Override: batch={_outboxBatchSize}, sweep={_outboxSweepIntervalSeconds}s"
                    : $"Defaults: batch={_outboxBatchSize}, sweep={_outboxSweepIntervalSeconds}s";
                _outboxDirty = false;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _outboxStatusMessage = $"Failed: {error}";
            }
        }
        catch (Exception ex)
        {
            _outboxStatusMessage = $"Error: {ex.Message}";
            Logger.LogError(ex, "Error saving SolarAssistant outbox runtime settings");
        }
    }

    private async Task ResetOutboxSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", OutboxOptionsAccessor.Value.ApiKey);
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { reset = true });

            if (response.IsSuccessStatusCode)
            {
                _outboxBatchSize = 50;
                _outboxSweepIntervalSeconds = 5;
                _outboxOverride = false;
                _outboxDirty = false;
                _outboxStatusMessage = "Reset to configured defaults.";
            }
        }
        catch (Exception ex)
        {
            _outboxStatusMessage = $"Reset error: {ex.Message}";
            Logger.LogError(ex, "Error resetting SolarAssistant outbox runtime settings");
        }
    }
}

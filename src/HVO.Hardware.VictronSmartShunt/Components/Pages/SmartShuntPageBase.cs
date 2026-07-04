using System.Net.Http.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using HVO.Hardware.VictronSmartShunt.Workers;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace HVO.Hardware.VictronSmartShunt.Components.Pages;

public abstract class SmartShuntPageBase : ComponentBase, IDisposable
{
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    [Inject] protected SmartShuntWorker Worker { get; set; } = default!;
    [Inject] protected PowerApiForwarder Forwarder { get; set; } = default!;
    [Inject] protected SmartShuntGatewayHealthService HealthService { get; set; } = default!;
    [Inject] protected IOptions<SmartShuntOptions> OptionsAccessor { get; set; } = default!;
    [Inject] protected IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] protected ILogger<SmartShuntPageBase> Logger { get; set; } = default!;

    protected int OutboxBatchSize { get; set; } = 50;
    protected int OutboxSweepIntervalSeconds { get; set; } = 5;
    protected bool OutboxDirty { get; set; }
    protected bool OutboxOverride { get; set; }
    protected string? OutboxStatusMessage { get; set; }

    protected SmartShuntOptions Options => OptionsAccessor.Value;
    protected SmartShuntDeviceSnapshot? LatestSnapshot => Worker.LastSnapshot;
    protected SmartShuntDeviceInfo? PrivateInfo => Worker.PrivateInfo;
    protected SmartShuntGatewayHealthSnapshot Health => HealthService.GetSnapshot();
    protected string DisplayAddress => string.IsNullOrWhiteSpace(Options.Address) ? "Not configured" : Options.Address;
    protected string ModeSummary => Options.PublicOnly ? "Public baseline" : Options.EnablePrivateEnrichment ? "Public + private enrichment" : "Custom mode";
    protected string CurrentDisplay => FormatCurrentWithFallback(LatestSnapshot);
    protected string HealthLabel => Health.State switch
    {
        "healthy" => "Healthy",
        "warning" => "Warning",
        "critical" => "Critical",
        _ => "Unknown"
    };
    protected string TelemetryModeBadgeClass => Options.EnablePrivateEnrichment && !Options.PublicOnly
        ? "smartshunt-badge-success"
        : Options.PublicOnly
            ? "smartshunt-badge-neutral"
            : "smartshunt-badge-warning";
    protected string PrivateEnrichmentStateText => Options.EnablePrivateEnrichment
        ? (PrivateInfo?.RecordedAtUtc.HasValue == true ? $"Active ({HvoFormat.Timestamp(PrivateInfo.RecordedAtUtc, "MMM d, HH:mm:ss")})" : "Enabled, waiting for overlay")
        : "Disabled by configuration";

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

    protected string RenderField<T>(T? value, Func<T?, string> formatter, bool isPrivateField = false)
    {
        if (value is not null)
            return formatter(value);

        if (isPrivateField && !Options.EnablePrivateEnrichment)
            return "Private disabled";

        return "Not reported";
    }

    protected string RenderPrivateText(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return Options.EnablePrivateEnrichment ? "Not reported" : "Private disabled";
    }

    protected static string FormatCount(uint? value) => value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "--";
    protected static string FormatMinutes(double? value) => value.HasValue ? $"{value.Value:0} min" : "--";
    protected static string FormatSeconds(int? value) => value.HasValue ? $"{value.Value} s" : "--";
    protected static string FormatKwh(double? value) => value.HasValue ? $"{value.Value:0.00} kWh" : "--";
    protected static string FormatBool(bool? value) => value.HasValue ? (value.Value ? "Yes" : "No") : "--";

    // Thin wrappers around HvoFormat for RenderField delegate compatibility
    protected static string FormatVolts(double? v) => HvoFormat.Voltage(v);
    protected static string FormatAmps(double? a) => HvoFormat.Current(a);
    protected static string FormatWatts(double? w) => HvoFormat.Power(w);
    protected static string FormatPercent(double? p) => HvoFormat.Percent(p);
    protected static string FormatAh(double? a) => HvoFormat.EnergyAh(a);
    protected static string FormatTemperature(double? t) => HvoFormat.Temperature(t);
    protected static string FormatTimestamp(DateTime? value) => HvoFormat.Timestamp(value, "MMM d, HH:mm:ss");

    protected static double ClampPercent(double? value, double min, double max)
    {
        if (!value.HasValue || max <= min)
            return 0;

        var normalized = (value.Value - min) / (max - min) * 100d;
        return Math.Clamp(normalized, 0d, 100d);
    }

    protected static string GaugeStyle(double progressPercent, string color)
        => $"--gauge-value:{progressPercent:0.##}; --gauge-color:{color};";

    protected static string FormatCurrentWithFallback(SmartShuntDeviceSnapshot? snapshot)
    {
        if (snapshot?.CurrentA is double currentA)
            return HvoFormat.Current(currentA);

        if (snapshot?.CurrentCoarseA is double coarseCurrentA)
            return $"{HvoFormat.Current(coarseCurrentA)} approx";

        return "--";
    }

    protected static Severity MapSeverity(SmartShuntGatewayHealthSeverity severity) => severity switch
    {
        SmartShuntGatewayHealthSeverity.Critical => Severity.Error,
        SmartShuntGatewayHealthSeverity.Warning => Severity.Warning,
        _ => Severity.Info,
    };

    protected async Task SaveOutboxSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { batchSize = OutboxBatchSize, sweepIntervalSeconds = OutboxSweepIntervalSeconds });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OutboxSettingsResponse>();
                OutboxOverride = result?.IsOverride ?? false;
                OutboxStatusMessage = OutboxOverride
                    ? $"Override: batch={OutboxBatchSize}, sweep={OutboxSweepIntervalSeconds}s"
                    : $"Defaults: batch={OutboxBatchSize}, sweep={OutboxSweepIntervalSeconds}s";
                OutboxDirty = false;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                OutboxStatusMessage = $"Failed: {error}";
            }
        }
        catch (Exception ex)
        {
            OutboxStatusMessage = $"Error: {ex.Message}";
            Logger.LogError(ex, "Error saving SmartShunt outbox runtime settings");
        }
    }

    protected async Task ResetOutboxSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { reset = true });

            if (response.IsSuccessStatusCode)
            {
                OutboxBatchSize = 50;
                OutboxSweepIntervalSeconds = 5;
                OutboxOverride = false;
                OutboxDirty = false;
                OutboxStatusMessage = "Reset to configured defaults.";
            }
        }
        catch (Exception ex)
        {
            OutboxStatusMessage = $"Reset error: {ex.Message}";
            Logger.LogError(ex, "Error resetting SmartShunt outbox runtime settings");
        }
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
}

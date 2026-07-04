using System.Net.Http.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Workers;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Status : IDisposable
{
    private const string PageHeadingText = "JK BMS fleet overview";
    private const string PageSummaryText = "Fleet-first monitoring with combined charge, balance, and connection health for the active JK BMS banks.";

    [Inject] private ILogger<Status> Logger { get; set; } = default!;
    [Inject] private BmsPollerWorker Poller { get; set; } = default!;
    [Inject] private ForwarderCoordinator Forwarder { get; set; } = default!;
    [Inject] private IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] private IOptions<OutboxOptions> OutboxOptions { get; set; } = default!;

    private IReadOnlyList<DevicePollState> Devices => Poller.DeviceStates;
    private IReadOnlyList<DevicePollState> ReportingDevices => Devices.Where(device => device.LatestReading is not null).ToList();
    private int TotalBanks => Devices.Count;
    private int ConnectedBanks => Devices.Count(device => device.IsSessionConnected);
    private int ReportingBanks => ReportingDevices.Count;
    private DateTime? LatestPollAtUtc => Devices.Where(device => device.LastPollAt.HasValue).Max(device => device.LastPollAt);
    private double? AverageStateOfChargePercent => ReportingDevices.Count > 0 ? ReportingDevices.Average(device => device.LatestReading!.StateOfChargePercent) : null;
    private double? AverageVoltageV => ReportingDevices.Count > 0 ? ReportingDevices.Average(device => device.LatestReading!.TotalVoltageMv / 1000d) : null;
    private double? TotalCurrentA => ReportingDevices.Count > 0 ? ReportingDevices.Sum(device => device.LatestReading!.CurrentMa / 1000d) : null;
    private double? TotalNominalCapacityAh => Devices.Where(device => device.LatestSettings is not null).Sum(device => device.LatestSettings!.NominalCapacityMah / 1000d);
    private double? AverageDeltaCellMv => ReportingDevices.Count > 0 ? ReportingDevices.Average(device => device.LatestReading!.DeltaCellVoltageMv) : null;
    private double? MaxBatteryTemperatureC => ReportingDevices.Count > 0 ? ReportingDevices.Max(device => device.LatestReading!.BatteryTemperature1C) : null;
    private DevicePollState? HighestSocBank => ReportingDevices.MaxBy(device => device.LatestReading!.StateOfChargePercent);
    private DevicePollState? LowestSocBank => ReportingDevices.MinBy(device => device.LatestReading!.StateOfChargePercent);
    private DevicePollState? HighestDeltaBank => ReportingDevices.MaxBy(device => device.LatestReading!.DeltaCellVoltageMv);
    private DevicePollState? HottestBank => ReportingDevices.MaxBy(device => device.LatestReading!.BatteryTemperature1C);
    private string AverageSocGaugeStyle => GaugeStyle(ClampPercent(AverageStateOfChargePercent, 0, 100), "var(--hvo-accent-success)");
    private string AverageVoltageGaugeStyle => GaugeStyle(ClampPercent(AverageVoltageV, 48, 58), "var(--hvo-accent-blue)");
    private string CurrentGaugeStyle => GaugeStyle(ClampPercent(TotalCurrentA is double current ? Math.Abs(current) : null, 0, 300), "var(--hvo-accent-amber)");

    protected override void OnInitialized()
    {
        Poller.DeviceStateChanged += OnStateChanged;
        Forwarder.SweepCompleted += OnStateChanged;
        Logger.LogInformation(
            "BMS status page loaded. {DeviceCount} device(s). Pending outbox: {Pending}",
            Poller.DeviceStates.Count, Forwarder.PendingCount);
    }

    public void Dispose()
    {
        Poller.DeviceStateChanged -= OnStateChanged;
        Forwarder.SweepCompleted -= OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private static double ClampPercent(double? value, double min, double max)
    {
        if (!value.HasValue || max <= min)
            return 0;

        var normalized = (value.Value - min) / (max - min) * 100d;
        return Math.Clamp(normalized, 0d, 100d);
    }

    private static string GaugeStyle(double progressPercent, string color)
        => $"--gauge-value:{progressPercent:0.##}; --gauge-color:{color};";

    private static string DisplayMillivolts(double? value)
        => value.HasValue ? $"{value.Value:0} mV" : "--";

    private static string DisplayMillivolts(ushort? value)
        => value.HasValue ? $"{value.Value} mV" : "--";

    private static string MiniGaugeStyle(double? percent)
        => $"width:{Math.Clamp(percent ?? 0, 0, 100):0.##}%;";

    private static string BankStatusLabel(DevicePollState device, CellInfoPacket? reading)
    {
        if (device.IsSessionConnected && reading is not null)
            return "Connected";

        if (reading?.HasAlarms == true)
            return "Alarm";

        if (device.ConsecutiveErrors > 0)
            return "Attention";

        return "Waiting";
    }

    private static string BankStatusClass(DevicePollState device, CellInfoPacket? reading)
    {
        if (device.IsSessionConnected && reading is not null)
            return "jk-health-dot-ok";

        if (reading?.HasAlarms == true)
            return "jk-health-dot-warn";

        if (device.ConsecutiveErrors > 0)
            return "jk-health-dot-error";

        return "jk-health-dot-idle";
    }

    private static string BankBadgeClass(DevicePollState device, CellInfoPacket? reading)
    {
        if (device.IsSessionConnected && reading is not null)
            return "badge-ok";

        if (reading?.HasAlarms == true)
            return "badge-warn";

        if (device.ConsecutiveErrors > 0)
            return "badge-error";

        return "badge-none";
    }

    private int _outboxBatchSize;
    private int _outboxSweepIntervalSeconds;
    private int _configuredOutboxBatchSize = 50;
    private int _configuredOutboxSweepIntervalSeconds = 5;
    private bool _outboxDirty;
    private bool _outboxOverride;
    private string? _outboxStatusMessage;

    private string FormatLastSent()
    {
        if (Forwarder.LastSentAt is { } lastSent)
            return HvoFormat.Timestamp(lastSent, "MMM d, yyyy - HH:mm:ss");
        return "--";
    }

    private async Task SaveOutboxSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", OutboxOptions.Value.ApiKey);
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { batchSize = _outboxBatchSize, sweepIntervalSeconds = _outboxSweepIntervalSeconds });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OutboxSettingsResponse>();
                _outboxOverride = result?.IsOverride ?? false;
                _outboxStatusMessage = _outboxOverride
                    ? $"Override active: batch={_outboxBatchSize}, sweep={_outboxSweepIntervalSeconds}s"
                    : $"Defaults: batch={_outboxBatchSize}, sweep={_outboxSweepIntervalSeconds}s";
                _outboxDirty = false;
                Logger.LogInformation("BMS outbox runtime settings saved: batch={BatchSize}, sweep={SweepIntervalSeconds}s",
                    _outboxBatchSize, _outboxSweepIntervalSeconds);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _outboxStatusMessage = $"Failed: {error}";
                Logger.LogWarning("Failed to save BMS outbox settings: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _outboxStatusMessage = $"Error: {ex.Message}";
            Logger.LogError(ex, "Error saving BMS outbox runtime settings");
        }

        StateHasChanged();
    }

    private async Task ResetOutboxSettingsAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", OutboxOptions.Value.ApiKey);
            var response = await client.PutAsJsonAsync("/diagnostics/outbox/settings",
                new { reset = true });

            if (response.IsSuccessStatusCode)
            {
                _outboxBatchSize = _configuredOutboxBatchSize;
                _outboxSweepIntervalSeconds = _configuredOutboxSweepIntervalSeconds;
                _outboxOverride = false;
                _outboxDirty = false;
                _outboxStatusMessage = "Reset to configured defaults.";
                Logger.LogInformation("BMS outbox runtime settings reset to defaults");
            }
        }
        catch (Exception ex)
        {
            _outboxStatusMessage = $"Reset error: {ex.Message}";
            Logger.LogError(ex, "Error resetting BMS outbox runtime settings");
        }

        StateHasChanged();
    }
}

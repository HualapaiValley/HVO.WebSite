using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using HVO.Hardware.VictronSmartShunt.SmartShunt.Health;
using HVO.Hardware.VictronSmartShunt.Workers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace HVO.Hardware.VictronSmartShunt.Components.Pages;

public partial class Status : IDisposable
{
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    [Inject] private SmartShuntWorker Worker { get; set; } = default!;
    [Inject] private PowerApiForwarder Forwarder { get; set; } = default!;
    [Inject] private SmartShuntGatewayHealthService HealthService { get; set; } = default!;
    [Inject] private IOptions<SmartShuntOptions> OptionsAccessor { get; set; } = default!;

    private SmartShuntOptions Options => OptionsAccessor.Value;
    private SmartShuntDeviceSnapshot? LatestSnapshot => Worker.LastSnapshot;
    private SmartShuntDeviceInfo? PrivateInfo => Worker.PrivateInfo;
    private SmartShuntGatewayHealthSnapshot Health => HealthService.GetSnapshot();
    private string DisplayAddress => string.IsNullOrWhiteSpace(Options.Address) ? "Not configured" : Options.Address;
    private string ModeSummary => Options.PublicOnly ? "Public telemetry" : Options.EnablePrivateEnrichment ? "Public + private" : "Custom";
    private string CurrentDisplay => FormatCurrentWithFallback(LatestSnapshot);

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

    private static string FormatTimestamp(DateTime? value) => value.HasValue ? value.Value.ToLocalTime().ToString("MMM d, HH:mm:ss") : "--";
    private static string FormatPercent(double? value) => value.HasValue ? $"{value.Value:0.0}%" : "--";
    private static string FormatVolts(double? value) => value.HasValue ? $"{value.Value:0.00} V" : "--";
    private static string FormatAmps(double? value) => value.HasValue ? $"{value.Value:0.000} A" : "--";
    private static string FormatWatts(double? value) => value.HasValue ? $"{value.Value:0} W" : "--";
    private static string FormatAh(double? value) => value.HasValue ? $"{value.Value:0.0} Ah" : "--";
    private static string FormatCount(uint? value) => value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "--";
    private static string FormatMinutes(double? value) => value.HasValue ? $"{value.Value:0} min" : "--";
    private static string FormatTemperature(double? value) => value.HasValue ? $"{value.Value:0} C" : "--";
    private static string FormatSeconds(int? value) => value.HasValue ? $"{value.Value} s" : "--";
    private static string FormatKwh(double? value) => value.HasValue ? $"{value.Value:0.00} kWh" : "--";
    private static string FormatBool(bool? value) => value.HasValue ? (value.Value ? "Yes" : "No") : "--";
    private static string FormatCurrentWithFallback(SmartShuntDeviceSnapshot? snapshot)
    {
        if (snapshot?.CurrentA is double currentA)
            return FormatAmps(currentA);

        if (snapshot?.CurrentCoarseA is double coarseCurrentA)
            return $"{FormatAmps(coarseCurrentA)} approx";

        return "--";
    }

    private static Severity MapSeverity(SmartShuntGatewayHealthSeverity severity) => severity switch
    {
        SmartShuntGatewayHealthSeverity.Critical => Severity.Error,
        SmartShuntGatewayHealthSeverity.Warning => Severity.Warning,
        _ => Severity.Info,
    };
}

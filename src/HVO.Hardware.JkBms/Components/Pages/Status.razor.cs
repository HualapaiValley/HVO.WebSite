using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Workers;
using HVO.WebSite.Themes.Components.Format;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Components.Pages;

public partial class Status : IDisposable
{
    private const string PageHeadingText = "JK BMS fleet overview";
    private const string PageSummaryText = "Fleet-first monitoring with combined charge, balance, and connection health for the active JK BMS banks.";

    [Inject] private ILogger<Status> Logger { get; set; } = default!;
    [Inject] private BmsPollerWorker Poller { get; set; } = default!;
    [Inject] private ForwarderCoordinator Forwarder { get; set; } = default!;

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
    private string AverageSocGaugeStyle => GaugeStyle(ClampPercent(AverageStateOfChargePercent, 0, 100), "#4ecdc4");
    private string AverageVoltageGaugeStyle => GaugeStyle(ClampPercent(AverageVoltageV, 48, 58), "#6ea8ff");
    private string CurrentGaugeStyle => GaugeStyle(ClampPercent(TotalCurrentA is double current ? Math.Abs(current) : null, 0, 300), "#ffb85c");

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
}

using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Telemetry;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Workers;

/// <summary>
/// Owns one JK BMS device session: its BLE transport, command serialization, polling,
/// reconnect/backoff state, and status updates.
///
/// Connection lifecycle:
///   The scan loop (run by <see cref="BmsPollerWorker"/>) calls
///   <see cref="SignalDeviceReadyAsync"/> when a BLE advertising report for this device
///   is seen. That hands the BlueZ Device object to the transport for GATT setup and
///   unblocks the poll loop. The poll loop runs until the transport goes disconnected
///   or a poll fails, then waits for the next <see cref="SignalDeviceReadyAsync"/> call.
/// </summary>
public sealed class JkBmsDevice : IAsyncDisposable
{
    private readonly BmsDeviceConfig _config;
    private readonly string _adapterName;
    private readonly JkBmsOptions _options;
    private readonly IBmsTransportFactory _transportFactory;
    private readonly IBluetoothAdapterCoordinator _adapterCoordinator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly BmsTelemetry _telemetry;
    private readonly ITelemetryService _telemetryService;
    private readonly Func<DevicePollState, JkBmsClient, CellInfoPacket, CancellationToken, Task> _onSuccessfulPoll;
    private readonly Action _onStateChanged;
    private readonly ILogger<JkBmsDevice> _logger;

    private JkBmsClient _client;

    public DevicePollState State { get; }
    public string Address => _config.Address;

    /// <summary>True if the transport's BLE link is currently up.</summary>
    public bool IsConnected => _client.IsConnected;

    public JkBmsDevice(
        BmsDeviceConfig config,
        string adapterName,
        DevicePollState state,
        JkBmsOptions options,
        IBmsTransportFactory transportFactory,
        IBluetoothAdapterCoordinator adapterCoordinator,
        ILoggerFactory loggerFactory,
        BmsTelemetry telemetry,
        ITelemetryService telemetryService,
        Func<DevicePollState, JkBmsClient, CellInfoPacket, CancellationToken, Task> onSuccessfulPoll,
        Action onStateChanged,
        ILogger<JkBmsDevice> logger)
    {
        _config = config;
        _adapterName = adapterName;
        State = state;
        _options = options;
        _transportFactory = transportFactory;
        _adapterCoordinator = adapterCoordinator;
        _loggerFactory = loggerFactory;
        _telemetry = telemetry;
        _telemetryService = telemetryService;
        _onSuccessfulPoll = onSuccessfulPoll;
        _onStateChanged = onStateChanged;
        _logger = logger;
        _client = CreateClient();
    }

    // ── Poll loop ─────────────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "JK BMS device session starting for {Alias} ({Address}) on {Adapter}",
            State.Alias, State.Address, _adapterName);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Requesting BLE session for {Alias} ({Address}) on {Adapter}",
                    State.Alias, State.Address, _adapterName);
                try
                {
                    await _adapterCoordinator.ConnectAsync(
                        _adapterName,
                        State.Address,
                        (device, token) => _client.ConnectWithDeviceAsync(device, token),
                        ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    State.ConsecutiveErrors++;
                    State.LastError = ex.Message;
                    State.BackoffLevel = Math.Min(State.BackoffLevel + 1, 10);
                    State.NextPollAt = DateTime.UtcNow.AddSeconds(Math.Min((int)Math.Pow(2, State.BackoffLevel), 30));
                    _onStateChanged();

                    _logger.LogWarning(
                        ex,
                        "BLE session request failed for {Alias} ({Address}); retrying",
                        State.Alias,
                        State.Address);

                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }

                _logger.LogInformation(
                    "BLE session established for {Alias} ({Address}); starting poll loop",
                    State.Alias, State.Address);

                await InitializeSessionMetadataAsync(ct);

                // Poll until the transport disconnects or an unrecoverable error occurs.
                await PollUntilDisconnectedAsync(ct);
            }
        }
        finally
        {
            _logger.LogInformation(
                "JK BMS device session stopped for {Alias} ({Address})",
                State.Alias, State.Address);
        }
    }

    private async Task PollUntilDisconnectedAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _client.IsConnected)
        {
            var wait = State.NextPollAt - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait, ct);

            if (DateTime.UtcNow < State.NextPollAt)
                continue;

            var stillConnected = await PollOnceAsync(ct);
            if (!stillConnected)
            {
                _logger.LogInformation(
                    "Transport disconnected for {Alias} ({Address}); resetting for reconnect",
                    State.Alias, State.Address);
                await ResetClientAsync();
                return;
            }
        }
    }

    private async Task InitializeSessionMetadataAsync(CancellationToken ct)
    {
        try
        {
            var deviceInfo = await _client.PollDeviceInfoAsync(ct);
            State.LatestDeviceInfo = deviceInfo;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Device-info refresh failed during session init for {Alias} ({Address}); continuing with steady-state polling",
                State.Alias,
                State.Address);
        }

        var freshSettings = _client.GetLatestSettings();
        if (freshSettings is not null)
            State.LatestSettings = freshSettings;

        _onStateChanged();
    }

    /// <returns>True if still connected after poll; false if the transport went down.</returns>
    private async Task<bool> PollOnceAsync(CancellationToken ct)
    {
        using var pollScope = _telemetryService.StartOperation("BMS.Poll");
        pollScope.WithTag("device", State.Alias);

        _logger.LogDebug("Polling {Alias} ({Address})", State.Alias, State.Address);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var packet = await PollCellInfoWithTimeoutAsync(ct);
            sw.Stop();

            _telemetry.DevicePollCount.Add(1,
                new KeyValuePair<string, object?>("device", State.Alias),
                new KeyValuePair<string, object?>("result", "success"));
            _telemetry.DevicePollDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("device", State.Alias));

            State.LatestReading = packet;
            State.LastPollAt = DateTime.UtcNow;
            State.ConsecutiveErrors = 0;
            State.BackoffLevel = 0;
            State.LastError = null;
            State.NextPollAt = DateTime.UtcNow.AddSeconds(State.PollIntervalSeconds);

            var freshSettings = _client.GetLatestSettings();
            if (freshSettings is not null)
                State.LatestSettings = freshSettings;

            await _onSuccessfulPoll(State, _client, packet, ct);

            pollScope
                .WithTag("soc_pct", packet.StateOfChargePercent)
                .WithTag("voltage_mv", packet.TotalVoltageMv)
                .WithTag("current_ma", packet.CurrentMa)
                .WithTag("alarms", packet.HasAlarms)
                .Succeed();

            _onStateChanged();

            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["CellVoltagesMv"] = string.Join(",", packet.CellVoltagesMv),
                ["CellCount"] = packet.CellCount,
            }))
            {
                _logger.LogInformation(
                    "Polled {Alias}: SOC={Soc}%, V={VoltageMv}mV, I={CurrentMa}mA, Δ={DeltaMv}mV, " +
                    "T1={T1C}°C T2={T2C}°C Tmos={TmosC}°C",
                    State.Alias,
                    packet.StateOfChargePercent,
                    packet.TotalVoltageMv,
                    packet.CurrentMa,
                    packet.DeltaCellVoltageMv,
                    packet.BatteryTemperature1C,
                    packet.BatteryTemperature2C,
                    packet.PowerTubeTemperatureC);
            }

            return true;
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            pollScope.Fail(ex);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();

            _telemetry.DevicePollCount.Add(1,
                new KeyValuePair<string, object?>("device", State.Alias),
                new KeyValuePair<string, object?>("result", "error"));
            _telemetry.DevicePollDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("device", State.Alias));

            State.ConsecutiveErrors++;
            State.LastError = ex.Message;
            State.BackoffLevel = Math.Min(State.BackoffLevel + 1, 10);

            // Use a shorter backoff here — the transport is likely disconnected;
            // the actual reconnect wait is driven by BLE re-advertisement, not a timer.
            var backoffSeconds = Math.Min((int)Math.Pow(2, State.BackoffLevel), 30);
            State.NextPollAt = DateTime.UtcNow.AddSeconds(backoffSeconds);

            pollScope.RecordException(ex);
            pollScope.Fail(ex);
            _onStateChanged();

            _logger.LogWarning(
                ex,
                "Poll failed for {Alias} ({Address}), error #{N}.",
                State.Alias, State.Address, State.ConsecutiveErrors);

            // If transport is gone, signal the caller to reset and re-await connect.
            return _client.IsConnected;
        }
    }

    private async Task<CellInfoPacket> PollCellInfoWithTimeoutAsync(CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_options.ExchangeTimeoutSeconds * 2 + 10);
        try
        {
            return await _client.PollCellInfoAsync(ct).WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            throw new JkBmsTimeoutException(State.Address);
        }
    }

    private async Task ResetClientAsync()
    {
        try { await _client.DisposeAsync(); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Non-fatal error resetting BLE client for {Alias} ({Address})", State.Alias, State.Address);
        }

        _client = CreateClient();
    }

    private JkBmsClient CreateClient()
    {
        var transport = _transportFactory.Create(_config.Address, _adapterName);
        return new JkBmsClient(transport, _loggerFactory.CreateLogger<JkBmsClient>());
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}

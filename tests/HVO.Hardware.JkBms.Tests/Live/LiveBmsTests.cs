using FluentAssertions;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.JkBms.Tests.Live;

/// <summary>
/// Live integration tests that connect to a real JK BMS device over Bluetooth LE.
///
/// These tests are skipped unless the <c>JK_BMS_LIVE_ADDRESS</c> environment variable
/// is set to the device's Bluetooth MAC address.
///
/// <code>
///   # Run live tests only:
///   JK_BMS_LIVE_ADDRESS=C8:47:8C:EC:1B:0F dotnet test --filter "TestCategory=Live"
/// </code>
///
/// Prerequisites:
///   - Must run on a Linux host (or devcontainer) with a BlueZ BLE adapter.
///   - The container/process must have the NET_ADMIN, NET_RAW capabilities.
///   - The target BMS must be powered on and advertising.
///
/// The transport is created and connected once in <see cref="ClassInitialize"/> and
/// shared across all tests. This avoids the rapid connect/disconnect churn that causes
/// HCI-level failures (le-connection-abort-by-local / HCI error 0x16) on BLE 4.0 hardware.
///
/// Scenarios covered:
/// <list type="bullet">
///   <item>Connection is established and maintained across the test class.</item>
///   <item>Cell voltages are within the LiFePO4 range [2500–3650 mV].</item>
///   <item>Pack voltage is approximately the sum of individual cell voltages (within 200 mV).</item>
///   <item>State of charge is in the range [0, 100].</item>
///   <item>Battery temperatures are in a plausible range [-20, 80] °C.</item>
///   <item>CRC validation passes on the received frame.</item>
/// </list>
/// </summary>
[TestClass]
[TestCategory("Live")]
public class LiveBmsTests
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(15);
    private static string? _address;
    private static JkBmsBluetoothTransport? _transport;
    private static string? _setupFailure;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _address = Environment.GetEnvironmentVariable("JK_BMS_LIVE_ADDRESS");
        if (string.IsNullOrWhiteSpace(_address))
            return; // individual tests call SkipIfNotLive

        _transport = new JkBmsBluetoothTransport(
            _address,
            connectTimeout: TimeSpan.FromSeconds(45),
            exchangeTimeout: TimeSpan.FromSeconds(10),
            NullLogger<JkBmsBluetoothTransport>.Instance);

        try
        {
            var adapter = await GetAdapterAsync(Environment.GetEnvironmentVariable("JK_BMS_LIVE_ADAPTER"));
            var device = await FindDeviceAsync(adapter, _address, CancellationToken.None);
            await _transport.ConnectWithDeviceAsync(device, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _setupFailure = $"Live BLE setup failed for {_address}: {ex.Message}";
        }
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_transport is not null)
        {
            await _transport.DisposeAsync();
            _transport = null;
        }
    }

    private static void SkipIfNotLive()
    {
        if (!string.IsNullOrWhiteSpace(_setupFailure))
            Assert.Inconclusive(_setupFailure);

        if (string.IsNullOrWhiteSpace(_address) || _transport is null)
            Assert.Inconclusive(
                "Set JK_BMS_LIVE_ADDRESS to run live tests " +
                "(e.g. JK_BMS_LIVE_ADDRESS=C8:47:8C:EC:1B:0F dotnet test --filter TestCategory=Live).");
    }

    private static async Task<Adapter> GetAdapterAsync(string? adapterName)
    {
        var adapters = await BlueZManager.GetAdaptersAsync();
        if (adapters.Count == 0)
            throw new InvalidOperationException("No BlueZ adapters were found.");

        if (!string.IsNullOrWhiteSpace(adapterName))
        {
            return adapters.FirstOrDefault(a =>
                       a.ObjectPath.ToString().EndsWith("/" + adapterName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Bluetooth adapter '{adapterName}' was not found.");
        }

        return adapters[0];
    }

    private static async Task<Device> FindDeviceAsync(Adapter adapter, string address, CancellationToken ct)
    {
        var knownDevices = await adapter.GetDevicesAsync();
        foreach (var knownDevice in knownDevices)
        {
            if (string.Equals(await knownDevice.GetAddressAsync(), address, StringComparison.OrdinalIgnoreCase))
                return knownDevice;
        }

        var seenDevice = new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnDeviceFound(Adapter sender, DeviceFoundEventArgs args)
        {
            _ = Task.Run(async () =>
            {
                if (string.Equals(await args.Device.GetAddressAsync(), address, StringComparison.OrdinalIgnoreCase))
                    seenDevice.TrySetResult(args.Device);
            }, CancellationToken.None);

            return Task.CompletedTask;
        }

        adapter.DeviceFound += OnDeviceFound;
        try
        {
            await adapter.StartDiscoveryAsync();
            using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            scanCts.CancelAfter(ScanTimeout);
            return await seenDevice.Task.WaitAsync(scanCts.Token);
        }
        finally
        {
            adapter.DeviceFound -= OnDeviceFound;
            try
            {
                await adapter.StopDiscoveryAsync();
            }
            catch
            {
            }
        }
    }

    // ── Connect lifecycle ─────────────────────────────────────────────────────

    [TestMethod]
    public void LiveConnect_IsConnectedAfterClassInitialize()
    {
        SkipIfNotLive();

        _transport!.IsConnected.Should().BeTrue(
            because: "ClassInitialize should have established the BLE connection");
    }

    // ── CRC validation ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LiveCellInfo_ResponseFrame_PassesCrcValidation()
    {
        SkipIfNotLive();

        byte[] frame = await _transport!.ExchangeAsync(
            JkBmsProtocol.BuildCellInfoCommand(),
            CancellationToken.None);

        JkBmsProtocol.ValidateCrc(frame).Should().BeTrue();
    }

    // ── Cell voltage plausibility ─────────────────────────────────────────────

    [TestMethod]
    public async Task LiveCellInfo_CellVoltages_WithinLiFePo4Range()
    {
        SkipIfNotLive();

        byte[] frame = await _transport!.ExchangeAsync(
            JkBmsProtocol.BuildCellInfoCommand(),
            CancellationToken.None);

        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        packet.CellCount.Should().BeInRange(1, 24);
        foreach (var v in packet.CellVoltagesMv)
            v.Should().BeInRange(2500, 3650,
                because: "LiFePO4 cells operate between 2.5 V and 3.65 V");
    }

    // ── Pack voltage plausibility ─────────────────────────────────────────────

    [TestMethod]
    public async Task LiveCellInfo_PackVoltage_ApproximatelySumOfCells()
    {
        SkipIfNotLive();

        byte[] frame = await _transport!.ExchangeAsync(
            JkBmsProtocol.BuildCellInfoCommand(),
            CancellationToken.None);

        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        uint sumOfCells = (uint)packet.CellVoltagesMv.Sum(v => (long)v);
        packet.TotalVoltageMv.Should().BeInRange(
            sumOfCells - 200, sumOfCells + 200,
            because: "pack voltage should be within 200 mV of the sum of individual cells");
    }

    // ── SOC plausibility ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task LiveCellInfo_StateOfCharge_InValidRange()
    {
        SkipIfNotLive();

        byte[] frame = await _transport!.ExchangeAsync(
            JkBmsProtocol.BuildCellInfoCommand(),
            CancellationToken.None);

        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        packet.StateOfChargePercent.Should().BeInRange(0, 100);
    }

    // ── Temperature plausibility ──────────────────────────────────────────────

    [TestMethod]
    public async Task LiveCellInfo_Temperatures_WithinPlausibleRange()
    {
        SkipIfNotLive();

        byte[] frame = await _transport!.ExchangeAsync(
            JkBmsProtocol.BuildCellInfoCommand(),
            CancellationToken.None);

        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        packet.BatteryTemperature1C.Should().BeInRange(-20.0, 80.0,
            because: "battery temperature should be in a physically plausible range");
        packet.PowerTubeTemperatureC.Should().BeInRange(-20.0, 100.0);
    }
}

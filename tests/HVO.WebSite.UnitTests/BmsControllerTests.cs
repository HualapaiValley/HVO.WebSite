using FluentAssertions;
using HVO.DataModels.Data;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HVO.WebSite.UnitTests;

/// <summary>
/// Unit tests for <see cref="BmsController"/> using SQLite in-memory (which supports
/// transactions) rather than the EF InMemory provider.
/// MSTest creates a new class instance per test method, so [TestInitialize] /
/// [TestCleanup] give each test a fully isolated database.
/// </summary>
[TestClass]
public class BmsControllerTests
{
    private const string DeviceA = "C8:47:8C:EC:1B:0A";
    private const string DeviceB = "C8:47:8C:EC:1B:0B";

    private SqliteConnection _conn = null!;
    private HvoV9DbContext _db = null!;
    private BmsController _ctrl = null!;

    [TestInitialize]
    public void Setup()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new HvoV9DbContext(
            new DbContextOptionsBuilder<HvoV9DbContext>()
                .UseSqlite(_conn)
                .Options);
        _db.Database.EnsureCreated();
        _ctrl = CreateController(_db);
    }

    [TestCleanup]
    public void Teardown()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private static JsonElement ToJsonElement<T>(IReadOnlyList<T> requests)
    {
        var json = JsonSerializer.Serialize(requests, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static BmsController CreateController(HvoV9DbContext db)
    {
        var ctrl = new BmsController(
            new BmsIngestService(db, NullLogger<BmsIngestService>.Instance),
            NullLogger<BmsController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        ctrl.ProblemDetailsFactory = new DefaultProblemDetailsFactory(
            Options.Create(new ApiBehaviorOptions()));
        return ctrl;
    }

    private static BmsIngestRequest MakeRequest(
        string deviceAddress = DeviceA,
        string recordedAt = "2026-01-01T00:00:00Z",
        long alarmBitmask = 0,
        int packVoltageMv = 52_000,
        int currentMa = 1_000,
        IReadOnlyList<int>? cellVoltagesMv = null,
        IReadOnlyList<int>? cellResistancesMOhm = null,
        BmsConfigRequest? config = null,
        BmsDeviceInfoRequest? deviceInfo = null) =>
        new()
        {
            Reading = new BmsReadingRequest
            {
                DeviceAddress = deviceAddress,
                DeviceAlias = "test-battery",
                RecordedAtUtc = DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
                PackVoltageMv = packVoltageMv,
                CurrentMa = currentMa,
                SocPercent = 80,
                SohPercent = 100,
                RemainingCapacityMah = 200_000,
                NominalCapacityMah = 250_000,
                CycleCount = 42,
                CycleCapacityMah = 10_000_000,
                BatteryTemperature1C = 25.0,
                BatteryTemperature2C = 25.5,
                PowerTubeTemperatureC = 30.0,
                BalancingActive = false,
                BalancingCurrentMa = 0,
                DeltaCellVoltageMv = 5,
                AlarmBitmask = alarmBitmask,
                CellVoltagesMv = cellVoltagesMv ?? [3250, 3250, 3250, 3250],
                CellResistancesMOhm = cellResistancesMOhm ?? [100, 101, 102, 103],
            },
            Config = config,
            DeviceInfo = deviceInfo,
        };

    private static BmsConfigRequest MakeConfig(byte cellCount = 4, bool chargingEnabled = true) =>
        new()
        {
            CellCount = cellCount,
            NominalCapacityMah = 250_000,
            ChargingEnabled = chargingEnabled,
            DischargingEnabled = true,
            BalancingEnabled = true,
            CellOvpMv = 3600,
            CellOvpRecoveryMv = 3500,
            CellUvpMv = 2800,
            CellUvpRecoveryMv = 2900,
            BalanceTriggerMv = 30,
            BalanceStartVoltageMv = 3300,
            ChargeOcpMa = 100_000,
            ChargeOcpDelayS = 5,
            ChargeOcpRecoveryS = 30,
            DischargeOcpMa = 200_000,
            DischargeOcpDelayS = 5,
            DischargeOcpRecoveryS = 30,
            ShortCircuitDelayUs = 150,
            ShortCircuitRecoveryS = 30,
            ChargeOtpC = 55.0,
            ChargeOtpRecoveryC = 50.0,
            ChargeUtpC = -10.0,
            ChargeUtpRecoveryC = 0.0,
            DischargeOtpC = 60.0,
            DischargeOtpRecoveryC = 55.0,
            MosOtpC = 80.0,
            MosOtpRecoveryC = 75.0,
        };

    private static BmsDeviceInfoRequest MakeDeviceInfo(
        string firmware = "1.0.0",
        string manufacturer = "JK") =>
        new()
        {
            Manufacturer = manufacturer,
            Hardware = "3.0",
            Firmware = firmware,
            SerialNumber = "SN001",
            DeviceName = "JK-B2A24S",
            ManufacturingDate = "2024-01",
            UserData = null,
        };

    // -------------------------------------------------------------------------
    // Empty batch
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_EmptyBatch_Returns400()
    {
        var result = await _ctrl.IngestReadings(ToJsonElement(new List<BmsIngestRequest>()), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task IngestReadings_NullReading_Returns400()
    {
        var result = await _ctrl.IngestReadings(
                ToJsonElement([new BmsIngestRequest { Reading = null! }]),
                CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.BmsDevices.Should().BeEmpty();
    }

    [TestMethod]
    public async Task IngestReadings_BlankDeviceAddress_Returns400()
    {
        var result = await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(deviceAddress: "   ")]),
                CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.BmsDevices.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Happy path — basic insert
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_SingleRecord_InsertsReadingAndCells()
    {
        var result = await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T00:00:00Z")]),
                CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Skipped.Should().Be(0);
        body.Failed.Should().BeEmpty();

        _db.BmsReadings.Count().Should().Be(1);
        _db.BmsCellVoltages.Count().Should().Be(4);
        _db.BmsCellResistances.Count().Should().Be(4);
    }

    [TestMethod]
    public async Task IngestReadings_MultipleDevices_RegistersEachDevice()
    {
        var batch = new[]
        {
            MakeRequest(DeviceA, "2026-01-01T00:00:00Z"),
            MakeRequest(DeviceB, "2026-01-01T00:00:00Z"),
        };

        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(2);

        _db.BmsDevices.Count().Should().Be(2);
        _db.BmsReadings.Count().Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Power calculation
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_PowerWatts_IsCalculatedCorrectly()
    {
        // 52V × 1A = 52W
        var req = MakeRequest(DeviceA, "2026-01-01T00:00:00Z");

        await _ctrl.IngestReadings(ToJsonElement(new List<BmsIngestRequest> { req }), CancellationToken.None);

        var reading = _db.BmsReadings.Single();
        reading.PowerWatts.Should().BeApproximately(52.0, 0.001);
    }

    [TestMethod]
    public async Task IngestReadings_DbFailure_PropagatesForGlobalExceptionHandling()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        await using var db = new ThrowOnSecondSaveHvoV9DbContext(
            new DbContextOptionsBuilder<HvoV9DbContext>()
                .UseSqlite(conn)
                .Options);
        await db.Database.EnsureCreatedAsync();
        var controller = CreateController(db);
        var request = MakeRequest(DeviceA, "2026-01-01T00:05:00Z");

        var action = () => controller.IngestReadings(
            ToJsonElement(new List<BmsIngestRequest> { request }),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("raw database detail");
    }

    [TestMethod]
    public async Task IngestReadings_HardwarePayloadAliases_PersistStateOfChargeAndHealth()
    {
        var recordedAt = DateTime.UtcNow.AddMinutes(-1);
        var json = $$"""
        {
          "reading": {
            "deviceAddress": "{{DeviceA}}",
            "deviceAlias": "bank-1a",
            "recordedAtUtc": "{{recordedAt:O}}",
            "totalVoltageMv": 53810,
            "currentMa": 7500,
            "stateOfChargePercent": 91,
            "stateOfHealthPercent": 98,
            "remainingCapacityMah": 227500,
            "nominalCapacityMah": 250000,
            "cycleCount": 42,
            "cycleCapacityMah": 10000000,
            "batteryTemperature1C": 25.0,
            "batteryTemperature2C": 25.5,
            "powerTubeTemperatureC": 30.0,
            "balancingActive": false,
            "balancingCurrentMa": 0,
            "deltaCellVoltageMv": 5,
            "alarmBitmask": 0,
            "cellVoltagesMv": [3361, 3364, 3362, 3363],
            "cellResistancesMOhm": [100, 101, 102, 103]
          }
        }
        """;
        var request = JsonSerializer.Deserialize<BmsIngestRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var result = await _ctrl.IngestReadings(ToJsonElement(new List<BmsIngestRequest> { request }), CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value.Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Failed.Should().BeEmpty();
        var reading = _db.BmsReadings.Single();
        reading.SocPercent.Should().Be(91);
        reading.SohPercent.Should().Be(98);
        reading.PackVoltageMv.Should().Be(53_810);
        _db.BmsCellVoltages.Should().HaveCount(4);

        var snapshot = await new PowerSystemSnapshotProvider(_db).GetLatestAsync(lookbackMinutes: 10, CancellationToken.None);
        var viewModel = PowerStatusViewModel.FromSnapshot(snapshot);
        viewModel.BatteryBanks.Should().ContainSingle();
        viewModel.BatteryBanks[0].StateOfCharge.Should().Be("91%");
        viewModel.BatteryBanks[0].Voltage.Should().Be("53.81 V");
    }

    // -------------------------------------------------------------------------
    // Idempotency
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_DuplicateRecord_SkipsSecondInsert()
    {
        var batch = new[] { MakeRequest(DeviceA, "2026-01-01T01:00:00Z") };

        await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(0);
        body.Skipped.Should().Be(1);

        _db.BmsReadings.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_IntraBatchDuplicate_SkipsSecondOccurrence()
    {
        // Two records with the same device + timestamp in the same batch
        var batch = new[]
        {
            MakeRequest(DeviceA, "2026-01-01T02:00:00Z"),
            MakeRequest(DeviceA, "2026-01-01T02:00:00Z"),
        };

        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Skipped.Should().Be(1);

        _db.BmsReadings.Count().Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // Config change detection
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_FirstConfig_InsertsSnapshot()
    {
        var cfg = MakeConfig();
        var req = MakeRequest(DeviceA, "2026-01-01T03:00:00Z", config: cfg);

        await _ctrl.IngestReadings(ToJsonElement(new List<BmsIngestRequest> { req }), CancellationToken.None);

        _db.BmsDeviceConfigs.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_SameConfigTwice_DoesNotInsertDuplicate()
    {
        var cfg = MakeConfig();

        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T04:00:00Z", config: cfg)]),
                CancellationToken.None);

        // Second call with identical config
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T04:00:30Z", config: cfg)]),
                CancellationToken.None);

        _db.BmsDeviceConfigs.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_ChangedConfig_InsertsNewSnapshot()
    {
        var cfg1 = MakeConfig(cellCount: 4, chargingEnabled: true);
        var cfg2 = MakeConfig(cellCount: 4, chargingEnabled: false);  // changed

        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T05:00:00Z", config: cfg1)]),
                CancellationToken.None);

        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T05:00:30Z", config: cfg2)]),
                CancellationToken.None);

        _db.BmsDeviceConfigs.Count().Should().Be(2);
    }

    [TestMethod]
    public async Task IngestReadings_NullConfig_DoesNotInsertSnapshot()
    {
        // No config payload — steady-state record
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T06:00:00Z", config: null)]),
                CancellationToken.None);

        _db.BmsDeviceConfigs.Count().Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // DeviceInfo change detection
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_FirstDeviceInfo_InsertsSnapshot()
    {
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T07:00:00Z", deviceInfo: MakeDeviceInfo())]),
                CancellationToken.None);

        _db.BmsDeviceInfos.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_SameDeviceInfoTwice_DoesNotInsertDuplicate()
    {
        var info = MakeDeviceInfo();

        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T07:00:00Z", deviceInfo: info)]),
                CancellationToken.None);

        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T07:00:30Z", deviceInfo: info)]),
                CancellationToken.None);

        _db.BmsDeviceInfos.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_ChangedDeviceInfo_InsertsNewSnapshot()
    {
        var info1 = MakeDeviceInfo(firmware: "1.0.0");
        var info2 = MakeDeviceInfo(firmware: "1.0.1");  // firmware updated

        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T07:01:00Z", deviceInfo: info1)]),
                CancellationToken.None);

        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T07:01:30Z", deviceInfo: info2)]),
                CancellationToken.None);

        _db.BmsDeviceInfos.Count().Should().Be(2);
    }

    [TestMethod]
    public async Task IngestReadings_NullDeviceInfo_DoesNotInsertSnapshot()
    {
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T07:02:00Z", deviceInfo: null)]),
                CancellationToken.None);

        _db.BmsDeviceInfos.Count().Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Alarm change detection
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_NewAlarm_OpensAlarmRow()
    {
        // First reading — alarm bit 0x01 appears
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T10:00:00Z", alarmBitmask: 0x01)]),
                CancellationToken.None);

        _db.BmsAlarms.Count().Should().Be(1);
        var alarm = _db.BmsAlarms.Single();
        alarm.AlarmBitmask.Should().Be(0x01);
        alarm.ClearedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task IngestReadings_AlarmClears_SetsAlarmClearedAt()
    {
        var clearTime = DateTime.Parse("2026-01-01T11:00:30Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        // Alarm fires
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T11:00:00Z", alarmBitmask: 0x02)]),
                CancellationToken.None);

        // Alarm clears
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T11:00:30Z", alarmBitmask: 0x00)]),
                CancellationToken.None);

        var alarm = _db.BmsAlarms.Single();
        alarm.ClearedAt.Should().NotBeNull();
        alarm.ClearedAt.Should().Be(clearTime);
    }

    [TestMethod]
    public async Task IngestReadings_NoAlarms_DoesNotCreateAlarmRow()
    {
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T12:00:00Z", alarmBitmask: 0x00)]),
                CancellationToken.None);

        _db.BmsAlarms.Count().Should().Be(0);
    }

    [TestMethod]
    public async Task IngestReadings_AlarmBitmaskChanges_ClosesOldAndOpensNew()
    {
        // First alarm
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T13:00:00Z", alarmBitmask: 0x01)]),
                CancellationToken.None);

        // Alarm bitmask changes (different bits) without clearing first
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T13:00:30Z", alarmBitmask: 0x04)]),
                CancellationToken.None);

        _db.BmsAlarms.Count().Should().Be(2);

        var alarms = _db.BmsAlarms.OrderBy(a => a.ActivatedAt).ToList();
        alarms[0].AlarmBitmask.Should().Be(0x01);
        alarms[0].ClearedAt.Should().NotBeNull();
        alarms[1].AlarmBitmask.Should().Be(0x04);
        alarms[1].ClearedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task IngestReadings_SameAlarmBitmask_DoesNotOpenDuplicateAlarm()
    {
        // Alarm fires
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T14:00:00Z", alarmBitmask: 0x01)]),
                CancellationToken.None);

        // Same alarm still active on next poll
        await _ctrl.IngestReadings(
                ToJsonElement([MakeRequest(DeviceA, "2026-01-01T14:00:30Z", alarmBitmask: 0x01)]),
                CancellationToken.None);

        // Only one open alarm row
        _db.BmsAlarms.Count().Should().Be(1);
        _db.BmsAlarms.Single().ClearedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task IngestReadings_OversizedBatch_ReturnsBadRequest()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var requests = Enumerable.Range(0, BmsController.MaxBatchSize + 1)
            .Select(i => MakeRequest(
                DeviceA,
                start.AddMinutes(i).ToString("yyyy-MM-ddTHH:mm:ssZ")))
            .ToList();

        var result = await _ctrl.IngestReadings(ToJsonElement(requests), CancellationToken.None);
        var objectResult = result.Result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private sealed class ThrowOnSecondSaveHvoV9DbContext(DbContextOptions<HvoV9DbContext> options) : HvoV9DbContext(options)
    {
        private int _saveCount;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCount++;
            if (_saveCount == 2)
            {
                throw new InvalidOperationException("raw database detail");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}

using FluentAssertions;
using HVO.DataModels.Data;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class BmsControllerTests
{
    private const string DeviceA = "C8:47:8C:EC:1B:0A";
    private const string DeviceB = "C8:47:8C:EC:1B:0B";

    private static HvoV9DbContext CreateDb() =>
        new(new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"BmsCtrl_{Guid.NewGuid()}")
            .Options);

    private static BmsController CreateController(HvoV9DbContext db)
    {
        var ctrl = new BmsController(db, NullLogger<BmsController>.Instance);
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
        BmsConfigRequest? config = null,
        BmsDeviceInfoRequest? deviceInfo = null) =>
        new()
        {
            Reading = new BmsReadingRequest
            {
                DeviceAddress = deviceAddress,
                DeviceAlias = "test-battery",
                RecordedAtUtc = DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
                PackVoltageMv = 52_000,
                CurrentMa = 1_000,
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
                CellVoltagesMv = [3250, 3250, 3250, 3250],
                CellResistancesMOhm = [100, 101, 102, 103],
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

    // -------------------------------------------------------------------------
    // Empty batch
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_EmptyBatch_Returns400()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var result = await ctrl.IngestReadings([], CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // -------------------------------------------------------------------------
    // Happy path — basic insert
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_SingleRecord_InsertsReadingAndCells()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var result = await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T00:00:00Z")],
            CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Skipped.Should().Be(0);
        body.Failed.Should().BeEmpty();

        db.BmsReadings.Count().Should().Be(1);
        db.BmsCellVoltages.Count().Should().Be(4);
        db.BmsCellResistances.Count().Should().Be(4);
    }

    [TestMethod]
    public async Task IngestReadings_MultipleDevices_RegistersEachDevice()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var batch = new[]
        {
            MakeRequest(DeviceA, "2026-01-01T00:00:00Z"),
            MakeRequest(DeviceB, "2026-01-01T00:00:00Z"),
        };

        var result = await ctrl.IngestReadings(batch, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(2);

        db.BmsDevices.Count().Should().Be(2);
        db.BmsReadings.Count().Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Power calculation
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_PowerWatts_IsCalculatedCorrectly()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        // 52V × 1A = 52W
        var req = MakeRequest(DeviceA, "2026-01-01T00:00:00Z");

        await ctrl.IngestReadings([req], CancellationToken.None);

        var reading = db.BmsReadings.Single();
        reading.PowerWatts.Should().BeApproximately(52.0, 0.001);
    }

    // -------------------------------------------------------------------------
    // Idempotency
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_DuplicateRecord_SkipsSecondInsert()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var batch = new[] { MakeRequest(DeviceA, "2026-01-01T01:00:00Z") };

        await ctrl.IngestReadings(batch, CancellationToken.None);

        var result = await ctrl.IngestReadings(batch, CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(0);
        body.Skipped.Should().Be(1);

        db.BmsReadings.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_IntraBatchDuplicate_SkipsSecondOccurrence()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        // Two records with the same device + timestamp in the same batch
        var batch = new[]
        {
            MakeRequest(DeviceA, "2026-01-01T02:00:00Z"),
            MakeRequest(DeviceA, "2026-01-01T02:00:00Z"),
        };

        var result = await ctrl.IngestReadings(batch, CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<BmsIngestBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Skipped.Should().Be(1);

        db.BmsReadings.Count().Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // Config change detection
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_FirstConfig_InsertsSnapshot()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var cfg = MakeConfig();
        var req = MakeRequest(DeviceA, "2026-01-01T03:00:00Z", config: cfg);

        await ctrl.IngestReadings([req], CancellationToken.None);

        db.BmsDeviceConfigs.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_SameConfigTwice_DoesNotInsertDuplicate()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var cfg = MakeConfig();

        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T04:00:00Z", config: cfg)],
            CancellationToken.None);

        // Second call with identical config
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T04:00:30Z", config: cfg)],
            CancellationToken.None);

        db.BmsDeviceConfigs.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_ChangedConfig_InsertsNewSnapshot()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var cfg1 = MakeConfig(cellCount: 4, chargingEnabled: true);
        var cfg2 = MakeConfig(cellCount: 4, chargingEnabled: false);  // changed

        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T05:00:00Z", config: cfg1)],
            CancellationToken.None);

        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T05:00:30Z", config: cfg2)],
            CancellationToken.None);

        db.BmsDeviceConfigs.Count().Should().Be(2);
    }

    [TestMethod]
    public async Task IngestReadings_NullConfig_DoesNotInsertSnapshot()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        // No config payload — steady-state record
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T06:00:00Z", config: null)],
            CancellationToken.None);

        db.BmsDeviceConfigs.Count().Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Alarm change detection
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestReadings_NewAlarm_OpensAlarmRow()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        // First reading — alarm bit 0x01 appears
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T10:00:00Z", alarmBitmask: 0x01)],
            CancellationToken.None);

        db.BmsAlarms.Count().Should().Be(1);
        var alarm = db.BmsAlarms.Single();
        alarm.AlarmBitmask.Should().Be(0x01);
        alarm.ClearedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task IngestReadings_AlarmClears_SetsAlarmClearedAt()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var activateTime = DateTime.Parse("2026-01-01T11:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var clearTime = DateTime.Parse("2026-01-01T11:00:30Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        // Alarm fires
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T11:00:00Z", alarmBitmask: 0x02)],
            CancellationToken.None);

        // Alarm clears
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T11:00:30Z", alarmBitmask: 0x00)],
            CancellationToken.None);

        var alarm = db.BmsAlarms.Single();
        alarm.ClearedAt.Should().NotBeNull();
        alarm.ClearedAt.Should().Be(clearTime);
    }

    [TestMethod]
    public async Task IngestReadings_NoAlarms_DoesNotCreateAlarmRow()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T12:00:00Z", alarmBitmask: 0x00)],
            CancellationToken.None);

        db.BmsAlarms.Count().Should().Be(0);
    }

    [TestMethod]
    public async Task IngestReadings_AlarmBitmaskChanges_ClosesOldAndOpensNew()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        // First alarm
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T13:00:00Z", alarmBitmask: 0x01)],
            CancellationToken.None);

        // Alarm bitmask changes (different bits) without clearing first
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T13:00:30Z", alarmBitmask: 0x04)],
            CancellationToken.None);

        db.BmsAlarms.Count().Should().Be(2);

        var alarms = db.BmsAlarms.OrderBy(a => a.ActivatedAt).ToList();
        alarms[0].AlarmBitmask.Should().Be(0x01);
        alarms[0].ClearedAt.Should().NotBeNull();
        alarms[1].AlarmBitmask.Should().Be(0x04);
        alarms[1].ClearedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task IngestReadings_SameAlarmBitmask_DoesNotOpenDuplicateAlarm()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        // Alarm fires
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T14:00:00Z", alarmBitmask: 0x01)],
            CancellationToken.None);

        // Same alarm still active on next poll
        await ctrl.IngestReadings(
            [MakeRequest(DeviceA, "2026-01-01T14:00:30Z", alarmBitmask: 0x01)],
            CancellationToken.None);

        // Only one open alarm row
        db.BmsAlarms.Count().Should().Be(1);
        db.BmsAlarms.Single().ClearedAt.Should().BeNull();
    }
}

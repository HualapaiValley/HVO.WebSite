using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.v9.Telemetry;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerIngestControllerTests
{
    private SqliteConnection _conn = null!;
    private HvoV9DbContext _db = null!;
    private PowerIngestTelemetry _telemetry = null!;
    private PowerIngestController _ctrl = null!;

    
    private static JsonElement ToJsonElement<T>(IReadOnlyList<T> requests)
    {
        var json = JsonSerializer.Serialize(requests, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

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
        _telemetry = new PowerIngestTelemetry();
        _ctrl = CreateController(_db, _telemetry);
    }

    [TestCleanup]
    public void Teardown()
    {
        _db.Dispose();
        _conn.Dispose();
        _telemetry.Dispose();
    }

    [TestMethod]
    public async Task IngestReadings_EmptyBatch_Returns400()
    {
        var result = await _ctrl.IngestReadings(ToJsonElement(new List<PowerReadingIngestRequest> {  }), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task IngestReadings_AllNew_InsertsAndReturns201()
    {
        var batch = new[]
        {
            MakeRequest("2026-05-23T01:00:00Z", pvPowerW: 1100),
            MakeRequest("2026-05-23T01:00:10Z", pvPowerW: 1200),
        };

        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<PowerReadingBatchResponse>().Subject;
        body.Inserted.Should().Be(2);
        body.Skipped.Should().Be(0);
        body.Failed.Should().BeEmpty();

        _db.PowerReadings.Count().Should().Be(2);
    }

    [TestMethod]
    public async Task IngestReadings_DuplicateRecords_SkipsExistingAndReturns201()
    {
        var batch = new[]
        {
            MakeRequest("2026-05-23T02:00:00Z", pvPowerW: 1300),
            MakeRequest("2026-05-23T02:00:10Z", pvPowerW: 1400),
        };

        await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);
        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<PowerReadingBatchResponse>().Subject;
        body.Inserted.Should().Be(0);
        body.Skipped.Should().Be(2);
        body.Failed.Should().BeEmpty();
        _db.PowerReadings.Count().Should().Be(2);
    }

    [TestMethod]
    public async Task IngestReadings_IntraBatchDuplicate_SkipsSecondOccurrence()
    {
        var batch = new[]
        {
            MakeRequest("2026-05-23T03:00:00Z", pvPowerW: 1500),
            MakeRequest("2026-05-23T03:00:00Z", pvPowerW: 1600),
        };

        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<PowerReadingBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Skipped.Should().Be(1);
        body.Failed.Should().BeEmpty();
        _db.PowerReadings.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_InvalidRecord_DeadLettersItAndInsertsRest()
    {
        var batch = new[]
        {
            MakeRequest("2026-05-23T04:00:00Z", pvPowerW: 1700),
            MakeRequest("2026-05-23T04:00:10Z", pvPowerW: 1800, sourceId: " ", batteryStateOfChargePercent: 150),
            MakeRequest("2026-05-23T04:00:20Z", pvPowerW: 1900),
        };

        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<PowerReadingBatchResponse>().Subject;
        body.Inserted.Should().Be(2);
        body.Skipped.Should().Be(0);
        body.Failed.Should().HaveCount(1);
        body.Failed[0].Error.Should().Contain("SourceId");
        _db.PowerReadings.Count().Should().Be(2);
    }

    [TestMethod]
    public async Task IngestReadings_MissingRecordedAt_DeadLettersRecord()
    {
        var batch = new[]
        {
            MakeRequest("2026-05-23T04:10:00Z", pvPowerW: 1700),
            MakeRequest(recordedAt: null, pvPowerW: 1800),
        };

        var result = await _ctrl.IngestReadings(ToJsonElement(batch), CancellationToken.None);

        var body = ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<PowerReadingBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Failed.Should().HaveCount(1);
        body.Failed[0].Error.Should().Contain("RecordedAtUtc");
        _db.PowerReadings.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_NormalizesIdentifiersAndOptionalStrings()
    {
        var request = MakeRequest(
            "2026-05-23T05:00:00Z",
            pvPowerW: 2000,
            sourceId: " solarassistant-total ",
            sourceSystem: " solarassistant ",
            deviceId: " total ",
            inverterMode: " online ");

        await _ctrl.IngestReadings(ToJsonElement(new List<PowerReadingIngestRequest> { request }), CancellationToken.None);

        var row = _db.PowerReadings.Single();
        row.SourceId.Should().Be("solarassistant-total");
        row.SourceSystem.Should().Be("solarassistant");
        row.DeviceId.Should().Be("total");
        row.InverterMode.Should().Be("online");
    }

    [TestMethod]
    public async Task GetRecentReadings_FiltersOrdersAndLimitsResults()
    {
        _db.PowerReadings.AddRange(
            MakeEntity("source-a", "2026-05-23T06:00:00Z", 1000),
            MakeEntity("source-b", "2026-05-23T06:00:10Z", 2000),
            MakeEntity("source-a", "2026-05-23T06:00:20Z", 3000),
            MakeEntity("source-a", "2026-05-23T06:00:30Z", 4000));
        await _db.SaveChangesAsync();

        var result = await _ctrl.GetRecentReadings(" source-a ", limit: 2, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeAssignableTo<IReadOnlyList<PowerReadingResponse>>().Subject;
        body.Should().HaveCount(2);
        body.Select(r => r.PvPowerW).Should().Equal(4000, 3000);
        body.All(r => r.SourceId == "source-a").Should().BeTrue();
    }

    [TestMethod]
    public async Task GetLatestSystemSnapshot_ComposesRecentSourceReadings()
    {
        var now = DateTime.UtcNow;
        var solarAssistant = MakeEntity("solarassistant-total", now.AddMinutes(-5), 1300, "solarassistant");
        solarAssistant.LoadPowerW = 875;
        solarAssistant.GridPowerW = -25;
        solarAssistant.BatteryStateOfChargePercent = 82;

        var smartShunt = MakeEntity("smartshunt-main", now.AddMinutes(-2), 0, "victron-smartshunt");
        smartShunt.BatteryVoltageV = 53.74;
        smartShunt.BatteryCurrentA = -20.72;
        smartShunt.BatteryPowerW = -1113;
        smartShunt.BatteryStateOfChargePercent = 0;

        _db.PowerReadings.AddRange(
            solarAssistant,
            smartShunt,
            MakeEntity("old-source", now.AddHours(-2), 9999, "solarassistant"));
        await _db.SaveChangesAsync();

        var result = await _ctrl.GetLatestSystemSnapshot(lookbackMinutes: 30, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<PowerSystemSnapshot>().Subject;
        body.Pv!.PowerW!.Value.Should().Be(1300);
        body.Ac!.GridFlowDirection!.Value.Should().Be(PowerFlowDirection.Export);
        body.Battery!.StateOfChargePercent!.Value.Should().Be(82);
        body.Battery.VoltageV!.Source.Should().Be(PowerMetricSource.VictronSmartShunt);
        body.Battery.FlowDirection!.Value.Should().Be(PowerFlowDirection.Charging);
    }

    [TestMethod]
    public async Task GetLatestSystemSnapshot_DoesNotDropSlowerSourceWhenOtherSourceHasManyRows()
    {
        var now = DateTime.UtcNow;
        var smartShunt = MakeEntity("smartshunt-main", now.AddMinutes(-20), 0, "victron-smartshunt");
        smartShunt.BatteryVoltageV = 53.7;
        smartShunt.BatteryPowerW = -900;
        _db.PowerReadings.Add(smartShunt);

        for (var i = 0; i < 600; i++)
        {
            _db.PowerReadings.Add(MakeEntity(
                $"solarassistant-{i}",
                now.AddSeconds(-i),
                pvPowerW: 1000 + i,
                sourceSystem: "solarassistant"));
        }

        await _db.SaveChangesAsync();

        var result = await _ctrl.GetLatestSystemSnapshot(lookbackMinutes: 60, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<PowerSystemSnapshot>().Subject;
        body.Pv!.PowerW!.Source.Should().Be(PowerMetricSource.SolarAssistant);
        body.Battery!.VoltageV!.Source.Should().Be(PowerMetricSource.VictronSmartShunt);
        body.Battery.VoltageV.Value.Should().Be(53.7);
    }

    [TestMethod]
    public async Task GetLatestSystemSnapshot_MatchesSourceSystemCaseInsensitively()
    {
        var now = DateTime.UtcNow;
        var solarAssistant = MakeEntity("solarassistant-total", now.AddMinutes(-5), 1400, "SolarAssistant");
        solarAssistant.LoadPowerW = 900;
        _db.PowerReadings.Add(solarAssistant);
        await _db.SaveChangesAsync();

        var result = await _ctrl.GetLatestSystemSnapshot(lookbackMinutes: 30, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<PowerSystemSnapshot>().Subject;
        body.Pv!.PowerW!.Value.Should().Be(1400);
        body.Ac!.LoadPowerW!.Value.Should().Be(900);
    }

    [TestMethod]
    public async Task GetLatestSystemSnapshot_IncludesLatestJkBmsBatteryBanksPerDevice()
    {
        var now = DateTime.UtcNow;
        var device1 = MakeBmsDevice("C8:47:8C:E4:56:B0", "bank-1a", now);
        var device2 = MakeBmsDevice("C8:47:8C:EC:1B:0F", "bank-2a", now);
        _db.BmsDevices.AddRange(device1, device2);
        await _db.SaveChangesAsync();

        _db.BmsReadings.AddRange(
            MakeBmsReading(device1.Id, now.AddMinutes(-20), 52000, alarmBitmask: 8),
            MakeBmsReading(device1.Id, now.AddMinutes(-2), 53810, alarmBitmask: 0),
            MakeBmsReading(device2.Id, now.AddMinutes(-25), 51000, alarmBitmask: 16),
            MakeBmsReading(device2.Id, now.AddMinutes(-3), 54210, alarmBitmask: 0));
        await _db.SaveChangesAsync();

        var latestDevice1 = _db.BmsReadings.Where(r => r.DeviceId == device1.Id).OrderByDescending(r => r.RecordedAt).First();
        var latestDevice2 = _db.BmsReadings.Where(r => r.DeviceId == device2.Id).OrderByDescending(r => r.RecordedAt).First();
        _db.BmsCellVoltages.AddRange(
            new BmsCellVoltage { ReadingId = latestDevice1.Id, CellIndex = 1, VoltageMv = 3361 },
            new BmsCellVoltage { ReadingId = latestDevice1.Id, CellIndex = 2, VoltageMv = 3364 },
            new BmsCellVoltage { ReadingId = latestDevice2.Id, CellIndex = 1, VoltageMv = 3388 },
            new BmsCellVoltage { ReadingId = latestDevice2.Id, CellIndex = 2, VoltageMv = 3392 });
        await _db.SaveChangesAsync();

        var result = await _ctrl.GetLatestSystemSnapshot(lookbackMinutes: 30, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<PowerSystemSnapshot>().Subject;
        body.BatteryBanks.Should().HaveCount(2);
        body.BatteryBanks![0].BankId.Should().Be("bank-1a");
        body.BatteryBanks[0].StateOfChargePercent!.Value.Should().Be(91);
        body.BatteryBanks[0].VoltageV!.Value.Should().Be(53.81);
        body.BatteryBanks[0].MinCellVoltageV!.Value.Should().Be(3.361);
        body.BatteryBanks[0].HasAlarms!.Value.Should().BeFalse();
        body.BatteryBanks[1].BankId.Should().Be("bank-2a");
        body.BatteryBanks[1].StateOfChargePercent!.Value.Should().Be(91);
        body.BatteryBanks[1].VoltageV!.Value.Should().Be(54.21);
        body.BatteryBanks[1].MinCellVoltageV!.Value.Should().Be(3.388);
        body.BatteryBanks[1].HasAlarms!.Value.Should().BeFalse();
        body.Battery!.BankCount!.Value.Should().Be(2);
    }

    [TestMethod]
    public async Task IngestDeviceInventory_PersistsAndLatestHandlesMissingAndStaleStates()
    {
        var missing = await _ctrl.GetLatestDeviceInventory("missing-source", staleAfterMinutes: 1, CancellationToken.None);
        ((OkObjectResult)missing.Result!).Value.Should().BeEquivalentTo(new { IsPresent = false, IsStale = true, SourceId = "missing-source" });

        var payload = new PowerDeviceInventoryPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            RestMetricCount = 124,
            MqttEntityCount = 48,
            MqttStateTopicCount = 42,
            Devices = [new PowerDeviceInventoryDevice { DeviceId = "eg4-6500ex", Name = "EG4 6500EX", Manufacturer = "EG4", Model = "6500EX", FirmwareVersion = "2026.1" }],
        };

        var ingest = await _ctrl.IngestDeviceInventory(payload, CancellationToken.None);
        var duplicate = await _ctrl.IngestDeviceInventory(new PowerDeviceInventoryPayload
        {
            SourceId = payload.SourceId,
            SourceSystem = payload.SourceSystem,
            DeviceId = payload.DeviceId,
            RecordedAtUtc = DateTime.UtcNow,
            RestMetricCount = payload.RestMetricCount,
            MqttEntityCount = payload.MqttEntityCount,
            MqttStateTopicCount = payload.MqttStateTopicCount,
            Devices = payload.Devices,
        }, CancellationToken.None);
        var latest = await _ctrl.GetLatestDeviceInventory("solarassistant-total", staleAfterMinutes: 1, CancellationToken.None);

        ((CreatedAtActionResult)ingest.Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        ((CreatedAtActionResult)duplicate.Result!).Value.Should().BeEquivalentTo(new { Inserted = false, Skipped = true });
        var body = ((OkObjectResult)latest.Result!).Value.Should().BeOfType<PowerDeviceInventorySnapshotResponse>().Subject;
        body.IsPresent.Should().BeTrue();
        body.IsStale.Should().BeTrue();
        body.Devices.Single().Model.Should().Be("6500EX");
    }

    [TestMethod]
    public async Task IngestConfiguration_PersistsReadOnlySettingsAndCommandInventory()
    {
        var payload = new PowerConfigurationPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            Settings = [new PowerConfigurationSetting { Key = "inverter_1.output_source_priority", Name = "Output source priority", Value = "Solar/Battery" }],
            CommandCapabilities = [new PowerCommandCapability { Key = "inverter_1.output_source_priority", Name = "Output source priority", CommandTopic = "solar_assistant/inverter_1/output_source_priority/set" }],
        };

        var ingest = await _ctrl.IngestConfiguration(payload, CancellationToken.None);
        var latest = await _ctrl.GetLatestConfiguration("solarassistant-total", staleAfterMinutes: 1440, CancellationToken.None);

        ((CreatedAtActionResult)ingest.Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        var body = ((OkObjectResult)latest.Result!).Value.Should().BeOfType<PowerConfigurationSnapshotResponse>().Subject;
        body.IsPresent.Should().BeTrue();
        body.IsStale.Should().BeFalse();
        body.Settings.Single().Name.Should().Be("Output source priority");
        body.CommandCapabilities.Single().CommandTopic.Should().EndWith("/set");
    }

    [TestMethod]
    public async Task IngestDeviceInventory_RejectsOversizedNestedStrings()
    {
        var result = await _ctrl.IngestDeviceInventory(new PowerDeviceInventoryPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            Devices = [new PowerDeviceInventoryDevice { DeviceId = "device-1", Name = new string('x', 257) }],
        }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.PowerDeviceInventorySnapshots.Should().BeEmpty();
    }

    [TestMethod]
    public async Task IngestConfiguration_RejectsOversizedNestedCollections()
    {
        var result = await _ctrl.IngestConfiguration(new PowerConfigurationPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            Settings = Enumerable.Range(0, 201)
                .Select(i => new PowerConfigurationSetting { Key = $"setting-{i}", Name = $"Setting {i}" })
                .ToArray(),
        }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.PowerConfigurationSnapshots.Should().BeEmpty();
    }

    [TestMethod]
    public async Task IngestEnergy_PersistsCountersAndLatestHandlesCounterResetFlag()
    {
        var payload = new PowerEnergyPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            CounterResetDetected = true,
            Counters = [new PowerEnergyCounter { Key = "pv_energy", Name = "PV energy", ValueKwh = 123.4, SourceTopic = "total/pv_energy" }],
        };

        var ingest = await _ctrl.IngestEnergy(payload, CancellationToken.None);
        var latest = await _ctrl.GetLatestEnergy("solarassistant-total", staleAfterMinutes: 1440, CancellationToken.None);

        ((CreatedAtActionResult)ingest.Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        var body = ((OkObjectResult)latest.Result!).Value.Should().BeOfType<PowerEnergySnapshotResponse>().Subject;
        body.IsPresent.Should().BeTrue();
        body.CounterResetDetected.Should().BeTrue();
        body.Counters.Single().ValueKwh.Should().Be(123.4);
    }

    [TestMethod]
    public async Task IngestInverterDetail_PersistsTypedDiagnosticDetails()
    {
        var payload = new PowerInverterDetailPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "inverter_1",
            RecordedAtUtc = DateTime.UtcNow,
            PvStrings = [new PowerPvStringDetail { StringId = "1", PowerW = 600, VoltageV = 120, CurrentA = 5 }],
            Load = new PowerInverterLoadDetail { LoadPowerW = 550, LoadApparentPowerVa = 700 },
            Battery = new PowerInverterBatteryDetail { PowerW = -200, VoltageV = 53.2 },
            TemperatureC = 31.2,
            Statuses = [new PowerInverterStatusDetail { Key = "inverter_1.status_1", Value = "normal", SourceTopic = "inverter_1/status_1" }],
        };

        var ingest = await _ctrl.IngestInverterDetail(payload, CancellationToken.None);
        var latest = await _ctrl.GetLatestInverterDetail("solarassistant-total", staleAfterMinutes: 1440, CancellationToken.None);

        ((CreatedAtActionResult)ingest.Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        var body = ((OkObjectResult)latest.Result!).Value.Should().BeOfType<PowerInverterDetailSnapshotResponse>().Subject;
        body.IsPresent.Should().BeTrue();
        body.PvStrings.Single().PowerW.Should().Be(600);
        body.Battery!.PowerW.Should().Be(-200);
        body.Statuses.Single().Value.Should().Be("normal");
    }

    [TestMethod]
    public async Task IngestEnergyAndInverterDetail_RejectInvalidRangesAndOversizedCollections()
    {
        var energy = await _ctrl.IngestEnergy(new PowerEnergyPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            Counters = [new PowerEnergyCounter { Key = "pv_energy", Name = "PV energy", ValueKwh = -1 }],
        }, CancellationToken.None);
        var inverter = await _ctrl.IngestInverterDetail(new PowerInverterDetailPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "inverter_1",
            RecordedAtUtc = DateTime.UtcNow,
            PvStrings = Enumerable.Range(0, 9).Select(i => new PowerPvStringDetail { StringId = i.ToString(), PowerW = 1 }).ToArray(),
        }, CancellationToken.None);

        energy.Result.Should().BeOfType<BadRequestObjectResult>();
        inverter.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.PowerEnergySnapshots.Should().BeEmpty();
        _db.PowerInverterDetailSnapshots.Should().BeEmpty();
    }

    [TestMethod]
    public async Task IngestGatewayStatus_PersistsRuntimeStatusAndLatestHandlesAlerts()
    {
        var observedAt = DateTime.UtcNow;
        var payload = new GatewayStatusPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = observedAt,
            Identity = new GatewayIdentity("solarassistant", "SolarAssistant Gateway", GatewayDomain.Power, "solarassistant-total", "total"),
            Health = new GatewayHealthSnapshot(
                GatewayHealthState.Warning,
                observedAt,
                [new GatewayHealthAlert("outbox-failed", GatewayAlertSeverity.Warning, "Historical failed outbox rows are present.")],
                GatewaySampleState.Live,
                OutboxState: "warning",
                ApiSyncState: "healthy"),
            Rest = new GatewayRuntimeSignal(GatewaySampleState.Live, observedAt, Detail: "124 REST metric(s)"),
            Mqtt = new GatewayRuntimeSignal(GatewaySampleState.Live, observedAt, Detail: "48 entit(ies), 42 state topic(s)"),
            Outbox = new GatewayOutboxStatus(PendingCount: 0, FailedCount: 71, LastSentAtUtc: observedAt, LastBatchCount: 1),
            RestMetricCount = 124,
            MqttEntityCount = 48,
            MqttStateTopicCount = 42,
            MqttCommandTopicCount = 14,
        };

        var ingest = await _ctrl.IngestGatewayStatus(payload, CancellationToken.None);
        var latest = await _ctrl.GetLatestGatewayStatus("solarassistant-total", staleAfterMinutes: 60, CancellationToken.None);

        ((CreatedAtActionResult)ingest.Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        var body = ((OkObjectResult)latest.Result!).Value.Should().BeOfType<GatewayStatusSnapshotResponse>().Subject;
        body.IsPresent.Should().BeTrue();
        body.Health!.State.Should().Be(GatewayHealthState.Warning);
        body.Health.Alerts.Single().Code.Should().Be("outbox-failed");
        body.Rest!.State.Should().Be(GatewaySampleState.Live);
        body.Outbox!.FailedCount.Should().Be(71);
        body.MqttCommandTopicCount.Should().Be(14);
    }

    [TestMethod]
    public async Task IngestGatewayStatus_RejectsOversizedAlertsAndNegativeCounts()
    {
        var payload = new GatewayStatusPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            Identity = new GatewayIdentity("solarassistant", "SolarAssistant Gateway", GatewayDomain.Power, "solarassistant-total", "total"),
            Health = new GatewayHealthSnapshot(
                GatewayHealthState.Warning,
                DateTime.UtcNow,
                Enumerable.Range(0, 51)
                    .Select(i => new GatewayHealthAlert($"alert-{i}", GatewayAlertSeverity.Warning, "warning"))
                    .ToArray(),
                GatewaySampleState.Live),
            Rest = new GatewayRuntimeSignal(GatewaySampleState.Live),
            Outbox = new GatewayOutboxStatus(PendingCount: -1, FailedCount: 0),
        };

        var result = await _ctrl.IngestGatewayStatus(payload, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.GatewayStatusSnapshots.Should().BeEmpty();
    }

    [TestMethod]
    public async Task IngestGatewayStatus_RejectsGatewayIdBeyondDbLimit()
    {
        var observedAt = DateTime.UtcNow;
        var payload = new GatewayStatusPayload
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = observedAt,
            Identity = new GatewayIdentity(new string('g', 65), "SolarAssistant Gateway", GatewayDomain.Power, "solarassistant-total", "total"),
            Health = new GatewayHealthSnapshot(GatewayHealthState.Healthy, observedAt, [], GatewaySampleState.Live),
            Rest = new GatewayRuntimeSignal(GatewaySampleState.Live),
            Outbox = new GatewayOutboxStatus(PendingCount: 0, FailedCount: 0),
        };

        var result = await _ctrl.IngestGatewayStatus(payload, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _db.GatewayStatusSnapshots.Should().BeEmpty();
    }

    private static PowerIngestController CreateController(HvoV9DbContext db, PowerIngestTelemetry telemetry)
    {
        var ctrl = new PowerIngestController(
            db,
            NullLogger<PowerIngestController>.Instance,
            new PowerReadingIngestService(db, telemetry, NullLogger<PowerReadingIngestService>.Instance),
            new PowerSystemSnapshotProvider(db),
            new PowerInventoryConfigurationProvider(db));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        ctrl.ProblemDetailsFactory = new DefaultProblemDetailsFactory(
            Options.Create(new ApiBehaviorOptions()));
        return ctrl;
    }

    private static PowerReadingIngestRequest MakeRequest(
        string? recordedAt,
        double pvPowerW,
        string sourceId = "solarassistant-total",
        string? sourceSystem = "solarassistant",
        string? deviceId = "total",
        double? batteryStateOfChargePercent = 82,
        string? inverterMode = null) => new()
    {
        SourceId = sourceId,
        SourceSystem = sourceSystem,
        DeviceId = deviceId,
        RecordedAtUtc = recordedAt is null
            ? default
            : DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
        PvPowerW = pvPowerW,
        LoadPowerW = 800,
        GridPowerW = -50,
        BatteryPowerW = -250,
        SystemPowerW = 1000,
        BatteryStateOfChargePercent = batteryStateOfChargePercent,
        BatteryVoltageV = 53.2,
        BatteryCurrentA = -4.7,
        GridVoltageV = 240,
        GridFrequencyHz = 60,
        OutputVoltageV = 120,
        OutputFrequencyHz = 60,
        LoadPercentage = 23,
        InverterMode = inverterMode,
    };

    private static PowerReading MakeEntity(string sourceId, string recordedAt, double pvPowerW) =>
        MakeEntity(sourceId, DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind), pvPowerW, "solarassistant");

    private static PowerReading MakeEntity(string sourceId, DateTime recordedAt, double pvPowerW, string sourceSystem) => new()
    {
        SourceId = sourceId,
        SourceSystem = sourceSystem,
        DeviceId = "total",
        RecordedAt = recordedAt,
        PvPowerW = pvPowerW,
        CreatedAt = DateTime.UtcNow,
    };

    private static BmsDevice MakeBmsDevice(string address, string alias, DateTime now) => new()
    {
        Address = address,
        Alias = alias,
        FirstSeenAt = now.AddDays(-1),
    };

    private static BmsReading MakeBmsReading(int deviceId, DateTime recordedAt, long packVoltageMv, long alarmBitmask) => new()
    {
        DeviceId = deviceId,
        RecordedAt = recordedAt,
        PackVoltageMv = packVoltageMv,
        CurrentMa = 7500,
        PowerWatts = packVoltageMv / 1000.0 * 7.5,
        SocPercent = 91,
        SohPercent = 100,
        BatteryTemp1C = 22.1,
        BatteryTemp2C = 22.4,
        PowerTubeC = 23.6,
        BalancingActive = false,
        BalancingCurrentMa = 0,
        DeltaCellVoltageMv = 3,
        AlarmBitmask = alarmBitmask,
    };
}

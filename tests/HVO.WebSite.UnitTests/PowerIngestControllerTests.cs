using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.v9.Telemetry;
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
        var result = await _ctrl.IngestReadings([], CancellationToken.None);

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

        var result = await _ctrl.IngestReadings(batch, CancellationToken.None);

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

        await _ctrl.IngestReadings(batch, CancellationToken.None);
        var result = await _ctrl.IngestReadings(batch, CancellationToken.None);

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

        var result = await _ctrl.IngestReadings(batch, CancellationToken.None);

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

        var result = await _ctrl.IngestReadings(batch, CancellationToken.None);

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

        var result = await _ctrl.IngestReadings(batch, CancellationToken.None);

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

        await _ctrl.IngestReadings([request], CancellationToken.None);

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

    private static PowerIngestController CreateController(HvoV9DbContext db, PowerIngestTelemetry telemetry)
    {
        var ctrl = new PowerIngestController(
            db,
            telemetry,
            NullLogger<PowerIngestController>.Instance,
            new PowerSystemSnapshotProvider(db));
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
}

using FluentAssertions;
using HVO.DataModels.Data;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.v9.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass]
[TestCategory("Integration")]
[TestCategory("SqlServerIntegration")]
[DoNotParallelize]
public sealed class PowerSnapshotConflictTests
{
    private static readonly DateTime At = new(2026, 8, 12, 17, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow("inventory", true)]
    [DataRow("inventory", false)]
    [DataRow("configuration", true)]
    [DataRow("configuration", false)]
    [DataRow("energy", true)]
    [DataRow("energy", false)]
    [DataRow("inverter", true)]
    [DataRow("inverter", false)]
    [DataRow("mppt", true)]
    [DataRow("mppt", false)]
    [DataRow("gateway", true)]
    [DataRow("gateway", false)]
    public async Task SnapshotRace_OnlyIdenticalCommittedContentIsAcknowledgedAndNextWriteHasCleanTracking(
        string kind, bool identical)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        using var telemetry = new PowerIngestTelemetry();
        var race = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var competitor = database.CreateContext();
            var response = await Ingest(Controller(competitor, telemetry), kind,
                identical ? "device" : "DEVICE", "snapshot-race", At, ct);
            ((ObjectResult)response.Result!).StatusCode.Should().Be(201);
        });
        await using var db = database.CreateContext(race);
        var controller = Controller(db, telemetry);
        var result = await Ingest(controller, kind, "device", "snapshot-race", At, CancellationToken.None);
        ((ObjectResult)result.Result!).StatusCode.Should().Be(identical ? 201 : 500);
        if (identical)
            ((PowerSnapshotIngestResponse)((ObjectResult)result.Result!).Value!).Skipped.Should().BeTrue();
        db.ChangeTracker.Entries().Should().NotContain(e => e.State == EntityState.Added);
        var following = await Ingest(controller, kind, "new-device", "following-source", At.AddMinutes(1), CancellationToken.None);
        ((PowerSnapshotIngestResponse)((ObjectResult)following.Result!).Value!).Inserted.Should().BeTrue();
        await using var verify = database.CreateContext();
        var count = kind switch
        {
            "inventory" => await verify.PowerDeviceInventorySnapshots.CountAsync(),
            "configuration" => await verify.PowerConfigurationSnapshots.CountAsync(),
            "energy" => await verify.PowerEnergySnapshots.CountAsync(),
            "inverter" => await verify.PowerInverterDetailSnapshots.CountAsync(),
            "mppt" => await verify.PowerMpptDetailSnapshots.CountAsync(),
            _ => await verify.GatewayStatusSnapshots.CountAsync()
        };
        count.Should().Be(2);
    }


    [TestMethod]
    [DataRow("inverter")]
    [DataRow("mppt")]
    public async Task MixedDetailBatch_PersistenceConflictReturnsRetryableFailureInsteadOfPermanentFailedItem(string kind)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        using var telemetry = new PowerIngestTelemetry();
        var interceptor = new BeforeSecondSaveInterceptor(async ct =>
        {
            await using var competitor = database.CreateContext();
            var committed = await Ingest(Controller(competitor, telemetry), kind, "DEVICE", "race", At, ct);
            ((ObjectResult)committed.Result!).StatusCode.Should().Be(201);
        });
        await using var db = database.CreateContext(interceptor);
        var controller = Controller(db, telemetry);
        var sources = new[] { "prefix", "race", "novel" };
        ActionResult<PowerReadingBatchResponse> result;
        if (kind == "mppt")
            result = await controller.IngestMpptDetailBatch(sources.Select(source => new PowerMpptDetailPayload
            {
                SourceId = source, SourceSystem = "eg4-mppt100-48hv", DeviceId = "device", RecordedAtUtc = At,
                Trackers = [new PowerMpptTrackerDetail { TrackerId = "pv", Name = "PV", PowerW = 1, Provenance = PowerObservationProvenance.Direct }]
            }).ToArray(), CancellationToken.None);
        else
            result = await controller.IngestInverterDetailBatch(sources.Select(source => new PowerInverterDetailPayload
            {
                SourceId = source, SourceSystem = "test", DeviceId = "device", RecordedAtUtc = At,
                PvStrings = [new PowerPvStringDetail { StringId = "pv", PowerW = 1 }]
            }).ToArray(), CancellationToken.None);
        ((ObjectResult)result.Result!).StatusCode.Should().Be(500);
        ((ObjectResult)result.Result!).Value.Should().BeOfType<ProblemDetails>();
        db.ChangeTracker.Entries().Should().NotContain(entry => entry.State == EntityState.Added);
        await using var verify = database.CreateContext();
        var committedSources = kind == "mppt"
            ? await verify.PowerMpptDetailSnapshots.Select(row => row.SourceId).ToListAsync()
            : await verify.PowerInverterDetailSnapshots.Select(row => row.SourceId).ToListAsync();
        committedSources.Should().BeEquivalentTo(new[] { "prefix", "race" });
    }

    private sealed class BeforeSecondSaveInterceptor(Func<CancellationToken, Task> competingWrite)
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int count;
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref count) == 2)
                await competingWrite(cancellationToken);
            return result;
        }
    }

    private static PowerIngestController Controller(HvoV9DbContext db, PowerIngestTelemetry telemetry) => new(
        db, NullLogger<PowerIngestController>.Instance,
        new PowerReadingIngestService(db, telemetry, NullLogger<PowerReadingIngestService>.Instance),
        new PowerSystemSnapshotProvider(db, Options.Create(new PowerCompositionOptions()), TimeProvider.System),
        new PowerInventoryConfigurationProvider(db, Options.Create(new PowerCompositionOptions()), TimeProvider.System))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        ProblemDetailsFactory = new DefaultProblemDetailsFactory(Options.Create(new ApiBehaviorOptions()))
    };

    private static Task<ActionResult<PowerSnapshotIngestResponse>> Ingest(
        PowerIngestController controller, string kind, string device, string source, DateTime at, CancellationToken ct) => kind switch
    {
        "inventory" => controller.IngestDeviceInventory(new PowerDeviceInventoryPayload
        {
            SourceId = source, SourceSystem = "test", DeviceId = device, RecordedAtUtc = at,
            Devices = [new PowerDeviceInventoryDevice { DeviceId = "child", Name = "Device" }]
        }, ct),
        "configuration" => controller.IngestConfiguration(new PowerConfigurationPayload
        {
            SourceId = source, SourceSystem = "test", DeviceId = device, RecordedAtUtc = at,
            Settings = [new PowerConfigurationSetting { Key = "mode", Name = "Mode", Value = "auto" }]
        }, ct),
        "energy" => controller.IngestEnergy(new PowerEnergyPayload
        {
            SourceId = source, SourceSystem = "test", DeviceId = device, RecordedAtUtc = at,
            Counters = [new PowerEnergyCounter { Key = "energy", Name = "Energy", ValueKwh = 1 }]
        }, ct),
        "inverter" => controller.IngestInverterDetail(new PowerInverterDetailPayload
        {
            SourceId = source, SourceSystem = "test", DeviceId = device, RecordedAtUtc = at,
            PvStrings = [new PowerPvStringDetail { StringId = "pv", PowerW = 1 }]
        }, ct),
        "mppt" => controller.IngestMpptDetail(new PowerMpptDetailPayload
        {
            SourceId = source, SourceSystem = "eg4-mppt100-48hv", DeviceId = device, RecordedAtUtc = at,
            Trackers = [new PowerMpptTrackerDetail { TrackerId = "pv", Name = "PV", PowerW = 1, Provenance = PowerObservationProvenance.Direct }]
        }, ct),
        _ => controller.IngestGatewayStatus(new GatewayStatusPayload
        {
            SourceId = source, SourceSystem = "test", DeviceId = device, RecordedAtUtc = at,
            Identity = new GatewayIdentity("gateway", "Gateway", GatewayDomain.Power, source, device),
            Health = new GatewayHealthSnapshot(GatewayHealthState.Healthy, at, [], GatewaySampleState.Live)
        }, ct)
    };
}

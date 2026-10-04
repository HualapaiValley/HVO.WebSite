using FluentAssertions;
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

[TestClass, TestCategory("Integration"), TestCategory("SqlServerIntegration"), DoNotParallelize]
public sealed class PowerObservationBatchInteractionTests
{
    [TestMethod]
    [DataRow("inverter"), DataRow("mppt")]
    public async Task SerialConflictingIdentity_KeepsMixedBatchRetryable_AndCorrectedReplayAccountsForEveryRecord(string kind)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        using var telemetry = new PowerIngestTelemetry();
        var options = Options.Create(new PowerCompositionOptions());
        var controller = new PowerIngestController(db, NullLogger<PowerIngestController>.Instance,
            new PowerReadingIngestService(db, telemetry, NullLogger<PowerReadingIngestService>.Instance),
            new PowerSystemSnapshotProvider(db), new PowerInventoryConfigurationProvider(db, options, TimeProvider.System))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new DefaultProblemDetailsFactory(Options.Create(new ApiBehaviorOptions())),
        };
        var at = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        async Task<ActionResult<PowerReadingBatchResponse>> Batch(params (string Source, string State)[] records) => kind switch
        {
            "inverter" => await controller.IngestInverterDetailBatch(records.Select(record => new PowerInverterDetailPayload
            {
                SourceId = record.Source, SourceSystem = "test", RecordedAtUtc = at,
                Statuses = [new() { Key = "mode", Value = record.State }], Load = new() { LoadPowerW = 100 },
            }).ToArray(), default),
            "mppt" => await controller.IngestMpptDetailBatch(records.Select(record => new PowerMpptDetailPayload
            {
                SourceId = record.Source, SourceSystem = "test", RecordedAtUtc = at,
                Trackers = [new() { TrackerId = "one", Name = record.State, PowerW = 100, Provenance = PowerObservationProvenance.Direct }],
            }).ToArray(), default),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        async Task<List<string>> Sources() => kind == "mppt"
            ? await db.PowerMpptDetailSnapshots.AsNoTracking().Select(row => row.SourceId).ToListAsync()
            : await db.PowerInverterDetailSnapshots.AsNoTracking().Select(row => row.SourceId).ToListAsync();
        static PowerReadingBatchResponse Response(ActionResult<PowerReadingBatchResponse> result) =>
            ((CreatedAtActionResult)result.Result!).Value.Should().BeOfType<PowerReadingBatchResponse>().Subject;

        Response(await Batch(("conflict", "StateA"))).Inserted.Should().Be(1);
        db.ChangeTracker.Clear();
        // The conflicting identity is already committed: this exercises #402's serial
        // content check, followed by #400's whole-batch retry boundary, without a race.
        var conflict = (await Batch(("prefix", "StateA"), ("conflict", "statea"), ("suffix", "StateA"))).Result
            .Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(500);
        conflict.Value.Should().BeOfType<ProblemDetails>();
        (await Sources()).Should().BeEquivalentTo(["prefix", "conflict"]);
        db.ChangeTracker.Entries().Should().NotContain(entry => entry.State == EntityState.Added);

        // Retry the prefix and the immutable committed payload, then the missing suffix.
        // Case-only changed content must never have been acknowledged under SQL collation.
        db.ChangeTracker.Clear();
        var recovered = Response(await Batch(("prefix", "StateA"), ("conflict", "StateA"), ("suffix", "StateA")));
        recovered.Inserted.Should().Be(1);
        recovered.Skipped.Should().Be(2);
        recovered.Failed.Should().BeEmpty();
        (await Sources()).Should().BeEquivalentTo(["prefix", "conflict", "suffix"]);
        var replay = Response(await Batch(("prefix", "StateA"), ("conflict", "StateA"), ("suffix", "StateA")));
        replay.Inserted.Should().Be(0);
        replay.Skipped.Should().Be(3);
        replay.Failed.Should().BeEmpty();
        (await Sources()).Should().HaveCount(3);
    }
}

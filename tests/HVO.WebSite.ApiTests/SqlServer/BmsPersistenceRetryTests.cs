using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass]
[TestCategory("Integration")]
[TestCategory("SqlServerIntegration")]
[DoNotParallelize]
public sealed class BmsPersistenceRetryTests
{
    private const string DeviceA = "00:11:22:33:44:55";
    private const string DeviceB = "00:11:22:33:44:66";
    private static readonly DateTime At = new(2026, 8, 12, 17, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PartialOverlap_ReconcilesCompleteChildrenAndKeepsMissingReadingRetryable(bool allCommitted)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var id = await SeedDevice(database);
        var race = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var other = database.CreateContext();
            other.BmsReadings.Add(Reading(id, 0));
            if (allCommitted) other.BmsReadings.Add(Reading(id, 1));
            await other.SaveChangesAsync(ct);
        });
        await using var db = database.CreateContext(race);
        var controller = Controller(db);
        var batch = JsonSerializer.SerializeToElement(new[] { Request(DeviceA, 0), Request(DeviceA, 1), Request(DeviceA, 1) }, JsonSerializerOptions.Web);
        var response = await controller.IngestReadings(batch, CancellationToken.None);
        ((ObjectResult)response.Result!).StatusCode.Should().Be(allCommitted ? 201 : 500);
        db.ChangeTracker.Entries().Should().NotContain(e => e.State == EntityState.Added);
        var retry = await controller.IngestReadings(batch, CancellationToken.None);
        var accounting = (BmsIngestBatchResponse)((ObjectResult)retry.Result!).Value!;
        (accounting.Inserted + accounting.Skipped).Should().Be(3);
        accounting.Failed.Should().BeEmpty();
        await using var verified = database.CreateContext();
        (await verified.BmsReadings.CountAsync()).Should().Be(2);
        (await verified.BmsCellVoltages.CountAsync()).Should().Be(4);
        (await verified.BmsCellResistances.CountAsync()).Should().Be(4);
    }

    [TestMethod]
    public async Task DeviceRegistrationOverlap_PreservesNovelDeviceAndDropsFailedAddedEntities()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var race = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var other = database.CreateContext();
            other.BmsDevices.Add(new BmsDevice { Address = DeviceA, Alias = "competitor", FirstSeenAt = At });
            await other.SaveChangesAsync(ct);
        });
        await using var db = database.CreateContext(race);
        var response = await Controller(db).IngestReadings(
            JsonSerializer.SerializeToElement(new[] { Request(DeviceA, 0), Request(DeviceB, 0) }, JsonSerializerOptions.Web), CancellationToken.None);
        ((BmsIngestBatchResponse)((ObjectResult)response.Result!).Value!).Inserted.Should().Be(2);
        db.ChangeTracker.Entries().Should().NotContain(e => e.State == EntityState.Added);
        await using var verified = database.CreateContext();
        (await verified.BmsDevices.CountAsync()).Should().Be(2);
        var readings = await verified.BmsReadings.ToListAsync();
        readings.Should().HaveCount(2).And.OnlyContain(r => r.DeviceId > 0);
        readings.Select(r => r.DeviceId).Distinct().Should().HaveCount(2);
    }

    [TestMethod]
    public async Task ChildConstraintFailure_RollsBackSummaryAndAllowsSameContextRetry()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await SeedDevice(database);
        var fault = new FailSecondSaveChildInterceptor();
        var capture = new CaptureSaveFailureInterceptor();
        await using var db = database.CreateContext(fault, capture);
        var controller = Controller(db);
        var batch = JsonSerializer.SerializeToElement(new[] { Request(DeviceA, 0) }, JsonSerializerOptions.Web);
        var attempt = () => controller.IngestReadings(batch, CancellationToken.None);
        await attempt.Should().ThrowAsync<DbUpdateException>();
        ((DbUpdateException)capture.Failure!).InnerException.Should().BeOfType<SqlException>().Which.Number.Should().Be(547);
        db.ChangeTracker.Entries().Should().BeEmpty();
        await using (var verified = database.CreateContext())
        {
            (await verified.BmsReadings.CountAsync()).Should().Be(0);
            (await verified.BmsCellVoltages.CountAsync()).Should().Be(0);
            (await verified.BmsCellResistances.CountAsync()).Should().Be(0);
            (await verified.BmsAlarms.CountAsync()).Should().Be(0);
        }
        var retry = await controller.IngestReadings(batch, CancellationToken.None);
        ((BmsIngestBatchResponse)((ObjectResult)retry.Result!).Value!).Inserted.Should().Be(1);
        await using var final = database.CreateContext();
        (await final.BmsReadings.CountAsync()).Should().Be(1);
        (await final.BmsCellVoltages.CountAsync()).Should().Be(2);
        (await final.BmsAlarms.CountAsync(a => a.ClearedAt == null)).Should().Be(1);
    }

    [TestMethod]
    public async Task PartialLegacySummary_IsNotAcknowledgedWithoutRequiredChildren()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var id = await SeedDevice(database);
        await using (var seed = database.CreateContext())
        {
            seed.BmsReadings.Add(new BmsReading { DeviceId = id, RecordedAt = At });
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext();
        var response = await Controller(db).IngestReadings(
            JsonSerializer.SerializeToElement(new[] { Request(DeviceA, 0) }, JsonSerializerOptions.Web), CancellationToken.None);
        ((ObjectResult)response.Result!).StatusCode.Should().Be(500);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [TestMethod]
    public async Task ConcurrentWriters_RebuildOrderedIntervalsAndLeaveOneCurrentAlarm()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await SeedDevice(database);
        await using var a = database.CreateContext();
        await using var b = database.CreateContext();
        await using var c = database.CreateContext();
        var responses = await Task.WhenAll(
            Service(a).IngestReadingsAsync([Request(DeviceA, 0, 0), Request(DeviceA, 2, 4)], CancellationToken.None),
            Service(b).IngestReadingsAsync([Request(DeviceA, 1, 1), Request(DeviceA, 4, 1)], CancellationToken.None),
            Service(c).IngestReadingsAsync([Request(DeviceA, 3, 0)], CancellationToken.None));
        responses.Sum(r => r.Inserted).Should().Be(5);
        await using var verified = database.CreateContext();
        var alarms = await verified.BmsAlarms.OrderBy(a => a.ActivatedAt).ToListAsync();
        alarms.Should().HaveCount(3);
        alarms[0].ActivatedAt.Should().Be(At.AddMinutes(1));
        alarms[0].ClearedAt.Should().Be(At.AddMinutes(2));
        alarms[1].ActivatedAt.Should().Be(At.AddMinutes(2));
        alarms[1].ClearedAt.Should().Be(At.AddMinutes(3));
        alarms[2].ActivatedAt.Should().Be(At.AddMinutes(4));
        alarms[2].ClearedAt.Should().BeNull();
        alarms.Should().OnlyContain(a => a.ClearedAt == null || a.ClearedAt >= a.ActivatedAt);
    }

    private static BmsIngestService Service(HvoV9DbContext db) => new(db, NullLogger<BmsIngestService>.Instance);
    private static BmsController Controller(HvoV9DbContext db) => new(Service(db), NullLogger<BmsController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        ProblemDetailsFactory = new DefaultProblemDetailsFactory(Options.Create(new ApiBehaviorOptions()))
    };
    private static async Task<int> SeedDevice(SqlServerDatabase database)
    {
        await using var db = database.CreateContext();
        var device = new BmsDevice { Address = DeviceA, Alias = "test", FirstSeenAt = At };
        db.BmsDevices.Add(device);
        await db.SaveChangesAsync();
        return device.Id;
    }
    private static BmsIngestRequest Request(string address, int minute, long alarm = 1) => new()
    {
        Reading = new BmsReadingRequest
        {
            DeviceAddress = address, DeviceAlias = "test", RecordedAtUtc = At.AddMinutes(minute),
            PackVoltageMv = 52000, AlarmBitmask = alarm, CellVoltagesMv = [3250, 3260], CellResistancesMOhm = [100, 101]
        }
    };
    private static BmsReading Reading(int id, int minute) => new()
    {
        DeviceId = id, RecordedAt = At.AddMinutes(minute), PackVoltageMv = 52000, AlarmBitmask = 1,
        CellVoltages = [new() { CellIndex = 1, VoltageMv = 3250 }, new() { CellIndex = 2, VoltageMv = 3260 }],
        CellResistances = [new() { CellIndex = 1, ResistanceMOhm = 100 }, new() { CellIndex = 2, ResistanceMOhm = 101 }]
    };
    private sealed class FailSecondSaveChildInterceptor : SaveChangesInterceptor
    {
        private int count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref count) == 2)
                ((HvoV9DbContext)data.Context!).BmsCellVoltages.Add(new BmsCellVoltage
                {
                    ReadingId = long.MaxValue, CellIndex = 255, VoltageMv = 0
                });
            return ValueTask.FromResult(result);
        }
    }
}

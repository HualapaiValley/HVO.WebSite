using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass]
[TestCategory("Integration")]
[TestCategory("SqlServerIntegration")]
[DoNotParallelize]
public sealed class CanonicalPersistenceTests
{
    private static readonly DateTime ObservedAt = new(2026, 8, 12, 17, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task CleanBootstrap_AppliesActualMigrationChainAndCanonicalIndexes()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue();
        new SqlConnectionStringBuilder(db.Database.GetConnectionString()).MultipleActiveResultSets.Should().BeTrue();

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT s.name + '.' + t.name + ':' + i.name
            FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE i.is_unique=1
            """;
        await db.Database.OpenConnectionAsync();
        var indexes = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                indexes.Add(reader.GetString(0));
        indexes.Should().Contain([
            "v9.PowerReading:IX_PowerReading_SourceId_RecordedAt",
            "v9.WeatherRaw:IX_WeatherRaw_StationId_RecordedAt",
            "v9.WeatherArchive:IX_WeatherArchive_StationId_RecordedAtUtc",
            "v9.BmsReading:IX_BmsReading_DeviceId_RecordedAt",
            "v9.SmartShuntDetailSnapshot:IX_SmartShuntDetailSnapshot_SourceId_RecordedAt"
        ]);
        command.CommandText = "SELECT COUNT(*) FROM v9.__EFMigrationsHistory";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(db.Database.GetMigrations().Count());
    }

    [TestMethod]
    [DataRow("20260528073744_AddGatewayStatusSnapshots")]
    [DataRow("20260812031452_AddSmartShuntDetailSnapshots")]
    public async Task SupportedPriorSchema_UpgradePreservesExistingRowsAndIdentities(string migration)
    {
        await using var database = await SqlServerDatabase.CreateAsync(migration);
        await using var db = database.CreateContext();
        var reading = Reading("upgrade-source", ObservedAt);
        var weather = new WeatherRaw { StationId = "upgrade-station", RecordedAt = ObservedAt, TemperatureF = 72.5 };
        db.PowerReadings.Add(reading);
        db.WeatherRaw.Add(weather);
        await db.SaveChangesAsync();
        var readingId = reading.Id;
        var weatherId = weather.Id;

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var persistedReading = await db.PowerReadings.SingleAsync();
        persistedReading.Id.Should().Be(readingId);
        persistedReading.SourceId.Should().Be("upgrade-source");
        persistedReading.BatteryPowerW.Should().Be(-260);
        var persistedWeather = await db.WeatherRaw.SingleAsync();
        persistedWeather.Id.Should().Be(weatherId);
        persistedWeather.TemperatureF.Should().Be(72.5);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
        // Exercise tables introduced after the earlier checkpoint through the current model.
        (await db.WeatherArchive.CountAsync()).Should().Be(0);
        (await db.SmartShuntDetailSnapshots.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task CanonicalLatestAndHistoryQueries_TranslateAndIsolateSources()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        db.PowerReadings.AddRange(Reading("smartshunt-test", ObservedAt.AddMinutes(-10)), Reading("smartshunt-test", ObservedAt));
        db.BmsReadings.Add(new BmsReading
        {
            Device = new BmsDevice { Address = "00:11:22:33:44:55", Alias = "test-bank", FirstSeenAt = ObservedAt },
            RecordedAt = ObservedAt, PackVoltageMv = 52000, CurrentMa = -5000, SocPercent = 80,
            CellVoltages = [new BmsCellVoltage { CellIndex = 1, VoltageMv = 3250 }]
        });
        foreach (var source in new[] { "mppt-test", "other-mppt" })
            foreach (var offset in new[] { -10, 0 })
            {
                var at = ObservedAt.AddMinutes(offset);
                var payload = new PowerMpptDetailPayload
                {
                    SourceId = source, SourceSystem = "eg4-mppt100-48hv",
                    DeviceId = "controller", RecordedAtUtc = at,
                    Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", PowerW = 500 }]
                };
                db.PowerMpptDetailSnapshots.Add(new PowerMpptDetailSnapshot
                {
                    SourceId = source, SourceSystem = payload.SourceSystem, DeviceId = payload.DeviceId,
                    RecordedAt = at, CreatedAt = at, TrackerCount = 1,
                    PayloadJson = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)
                });
            }
        db.WeatherRaw.AddRange(
            new WeatherRaw { StationId = "davis-test", RecordedAt = ObservedAt, TemperatureF = 75 },
            new WeatherRaw { StationId = "other-station", RecordedAt = ObservedAt, TemperatureF = 90 });
        await db.SaveChangesAsync();

        var clock = new FixedTimeProvider(ObservedAt.AddSeconds(1));
        var composition = Options.Create(new PowerCompositionOptions());
        var current = await new PowerSystemSnapshotProvider(db, composition, clock).GetLatestAsync();
        current.Should().NotBeNull();
        current!.ObservedAtUtc.Should().Be(clock.GetUtcNow().UtcDateTime);
        current.BatteryObservations.Should().ContainSingle(row => row.SourceId == "smartshunt-test")
            .Which.ObservedAtUtc.Should().Be(ObservedAt);
        var history = await new PowerInventoryConfigurationProvider(db, composition, clock)
            .GetRecentTelemetryAsync(["mppt-test"], ["smartshunt-test"], ObservedAt.AddMinutes(-20));
        history.MpptDetails.Should().HaveCount(2).And.OnlyContain(row => row.SourceId == "mppt-test");
        history.BatteryReadings.Should().HaveCount(2).And.OnlyContain(row => row.SourceId == "smartshunt-test");
        var latestWeather = await db.WeatherRaw.AsNoTracking().Where(row => row.StationId == "davis-test")
            .OrderByDescending(row => row.RecordedAt).ThenByDescending(row => row.Id)
            .Select(row => new { row.RecordedAt, row.TemperatureF }).FirstAsync();
        latestWeather.RecordedAt.Should().Be(ObservedAt);
        latestWeather.TemperatureF.Should().Be(75);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompetingDuplicateOrPartialOverlap_ExposesSqlConflictAndRollsBackNovelRows(bool partialOverlap)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var competingWrite = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var competing = database.CreateContext();
            competing.PowerReadings.Add(Reading("overlap-test", ObservedAt));
            await competing.SaveChangesAsync(ct);
        });
        await using var db = database.CreateContext(competingWrite);
        (await db.PowerReadings.CountAsync()).Should().Be(0);
        db.PowerReadings.Add(Reading("overlap-test", ObservedAt));
        if (partialOverlap)
            db.PowerReadings.Add(Reading("overlap-test", ObservedAt.AddSeconds(15)));

        var save = () => db.SaveChangesAsync();
        var failure = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        failure.InnerException.Should().BeOfType<SqlException>().Which.Number.Should().BeOneOf(2601, 2627);
        db.ChangeTracker.Clear();

        await using var verification = database.CreateContext();
        var rows = await verification.PowerReadings.AsNoTracking().ToArrayAsync();
        rows.Should().ContainSingle().Which.RecordedAt.Should().Be(ObservedAt);
        // This demonstrates why #400 must not acknowledge the rolled-back novel identity.
        if (partialOverlap)
            (await verification.PowerReadings.AnyAsync(row => row.RecordedAt == ObservedAt.AddSeconds(15))).Should().BeFalse();
    }

    [TestMethod]
    public async Task SmartShuntCompetingReplay_IsConservativelyReconciledAndRemainsIdempotent()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var competingWrite = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var competing = database.CreateContext();
            var response = await Controller(competing).IngestBatch(Batch(ObservedAt), ct);
            response.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
        });
        await using var db = database.CreateContext(competingWrite);

        var response = await Controller(db).IngestBatch(Batch(ObservedAt), CancellationToken.None);
        var result = response.Result.Should().BeOfType<ObjectResult>().Which;
        result.StatusCode.Should().Be(201);
        var body = result.Value.Should().BeOfType<PowerReadingBatchResponse>().Which;
        body.Inserted.Should().Be(0);
        body.Skipped.Should().Be(1);
        body.Failed.Should().BeEmpty();
        // Reuse the same context to ensure conflict cleanup leaves it usable.
        var next = await Controller(db).IngestBatch(Batch(ObservedAt.AddSeconds(15)), CancellationToken.None);
        next.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
        (await db.PowerReadings.CountAsync()).Should().Be(2);
        (await db.SmartShuntDetailSnapshots.CountAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task SmartShuntDetailFailure_AtomicallyRollsBackSummaryAndSupportsRetry()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using (var setup = database.CreateContext())
            await setup.Database.ExecuteSqlRawAsync("""
                ALTER TABLE v9.SmartShuntDetailSnapshot ADD CONSTRAINT CK_test_detail_failure
                CHECK (ConsumedAh > 0);
                """);
        var fault = new CaptureSaveFailureInterceptor();
        await using (var db = database.CreateContext(fault))
        {
            var response = await Controller(db).IngestBatch(Batch(ObservedAt), CancellationToken.None);
            response.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
            fault.Failure.Should().BeOfType<DbUpdateException>().Which.InnerException
                .Should().BeOfType<SqlException>().Which.Number.Should().Be(547);
        }

        await using var verification = database.CreateContext();
        (await verification.PowerReadings.CountAsync()).Should().Be(0);
        (await verification.SmartShuntDetailSnapshots.CountAsync()).Should().Be(0);
        await verification.Database.ExecuteSqlRawAsync("ALTER TABLE v9.SmartShuntDetailSnapshot DROP CONSTRAINT CK_test_detail_failure");
        var retry = await Controller(verification).IngestBatch(Batch(ObservedAt), CancellationToken.None);
        retry.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
        (await verification.PowerReadings.CountAsync()).Should().Be(1);
        (await verification.SmartShuntDetailSnapshots.CountAsync()).Should().Be(1);
    }

    private static PowerReading Reading(string source, DateTime at) => new()
    {
        SourceId = source, SourceSystem = "victron-smartshunt", DeviceId = "battery", RecordedAt = at,
        BatteryVoltageV = 52, BatteryCurrentA = -5, BatteryPowerW = -260,
        BatteryStateOfChargePercent = 80, CreatedAt = at
    };

    private static SmartShuntObservationIngestController Controller(HvoV9DbContext db) => new(db,
        NullLogger<SmartShuntObservationIngestController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("source", "smartshunt-test")], "test"))
            }
        }
    };

    private static JsonElement Batch(DateTime at) => JsonSerializer.SerializeToElement(new[]
    {
        new SmartShuntObservationPayload(new PowerReadingPayload
        {
            SourceId = "smartshunt-test", SourceSystem = "victron-smartshunt", DeviceId = "battery",
            RecordedAtUtc = at, BatteryVoltageV = 52, BatteryCurrentA = -5, BatteryPowerW = -260,
            BatteryStateOfChargePercent = 80
        }, new SmartShuntDetailPayload
        {
            SourceId = "smartshunt-test", SourceSystem = "victron-smartshunt", DeviceId = "battery",
            RecordedAtUtc = at, ConsumedAh = -20, RemainingMinutes = 90
        })
    }, JsonSerializerOptions.Web);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

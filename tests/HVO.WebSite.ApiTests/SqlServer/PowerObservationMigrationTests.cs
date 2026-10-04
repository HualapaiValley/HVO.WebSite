using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.v9.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass, TestCategory("Integration"), TestCategory("SqlServerIntegration"), DoNotParallelize]
public sealed class PowerObservationMigrationTests
{
    private static readonly string[] Tables = ["PowerDeviceInventorySnapshot", "PowerConfigurationSnapshot", "PowerEnergySnapshot", "PowerInverterDetailSnapshot", "GatewayStatusSnapshot"];
    private const string Prior = "20260812031452_AddSmartShuntDetailSnapshots";
    private static readonly DateTime At = new(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow("configuration"), DataRow("inverter"), DataRow("mppt")]
    public async Task ActualSqlIngestAndLatestQueries_PreserveRecurringStateAndFreshness_AndRejectConflictingReplay(string kind)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        using var telemetry = new PowerIngestTelemetry();
        var clock = new Clock();
        var options = Options.Create(new PowerCompositionOptions());
        var controller = new PowerIngestController(db, NullLogger<PowerIngestController>.Instance,
            new PowerReadingIngestService(db, telemetry, NullLogger<PowerReadingIngestService>.Instance),
            new PowerSystemSnapshotProvider(db), new PowerInventoryConfigurationProvider(db, options, clock), clock, options)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new DefaultProblemDetailsFactory(Options.Create(new ApiBehaviorOptions())),
        };
        async Task<ActionResult<PowerSnapshotIngestResponse>> Ingest(DateTime at, string state, string source = "source") => kind switch
        {
            "configuration" => await controller.IngestConfiguration(new()
            {
                SourceId = source, SourceSystem = "test", RecordedAtUtc = at,
                Settings = [new() { Key = "mode", Name = "Mode", Value = state }],
            }, default),
            "inverter" => await controller.IngestInverterDetail(new()
            {
                SourceId = source, SourceSystem = "test", RecordedAtUtc = at,
                Statuses = [new() { Key = "mode", Value = state }],
                Load = new() { LoadPowerW = 100 },
            }, default),
            "mppt" => await controller.IngestMpptDetail(new()
            {
                SourceId = source, SourceSystem = "test", RecordedAtUtc = at,
                Trackers = [new() { TrackerId = "one", Name = state, PowerW = 100, Provenance = PowerObservationProvenance.Direct }],
            }, default),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        async Task<JsonElement> Latest() => JsonSerializer.SerializeToElement(kind switch
        {
            "configuration" => ((OkObjectResult)(await controller.GetLatestConfiguration("source", 5)).Result!).Value,
            "inverter" => ((OkObjectResult)(await controller.GetLatestInverterDetail("source", 5)).Result!).Value,
            "mppt" => ((OkObjectResult)(await controller.GetLatestMpptDetail("source", 5)).Result!).Value,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        }, JsonSerializerOptions.Web);
        string State(JsonElement result) => kind switch
        {
            "configuration" => result.GetProperty("settings")[0].GetProperty("value").GetString()!,
            "inverter" => result.GetProperty("statuses")[0].GetProperty("value").GetString()!,
            "mppt" => result.GetProperty("trackers")[0].GetProperty("name").GetString()!,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        foreach (var (at, state) in new[] { (At.AddMinutes(-10), "StateA"), (At.AddMinutes(-1), "StateB"), (At, "StateA") })
            ((CreatedAtActionResult)(await Ingest(at, state)).Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        var latest = await Latest();
        latest.GetProperty("recordedAtUtc").GetDateTime().Should().Be(At);
        State(latest).Should().Be("StateA");
        latest.GetProperty("isStale").GetBoolean().Should().BeFalse();
        db.ChangeTracker.Clear();
        ((CreatedAtActionResult)(await Ingest(At, "StateA")).Result!).Value.Should().BeEquivalentTo(new { Inserted = false, Skipped = true });
        // SQL's default case-insensitive collation must not turn changed JSON into an acknowledgement.
        (await Ingest(At, "statea")).Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
        State(await Latest()).Should().Be("StateA");
        ((CreatedAtActionResult)(await Ingest(At.AddHours(1), "StateB")).Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        ((CreatedAtActionResult)(await Ingest(At, "StateB", "other-source")).Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        (await Latest()).GetProperty("recordedAtUtc").GetDateTime().Should().Be(At);
        State(await Latest()).Should().Be("StateA");
        clock.Now = At.AddMinutes(6);
        (await Latest()).GetProperty("isStale").GetBoolean().Should().BeTrue();
        ((CreatedAtActionResult)(await Ingest(clock.Now, "StateA")).Result!).Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
        (await Latest()).GetProperty("isStale").GetBoolean().Should().BeFalse();
        (await Latest()).GetProperty("recordedAtUtc").GetDateTime().Should().Be(clock.Now);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = At;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("20260528073744_AddGatewayStatusSnapshots")]
    [DataRow(Prior)]
    public async Task CleanAndSupportedPriorSchemas_PreserveRowsAndReplaceOnlyHistoricalHashUniqueness(string? prior)
    {
        await using var database = await SqlServerDatabase.CreateAsync(prior);
        await using var db = database.CreateContext();
        // Both supported checkpoints contain all five affected tables.
        foreach (var table in Tables)
        {
            db.Add(Seed(table, "source", At, "a"));
            db.Add(Seed(table, "source", At.AddSeconds(1), "b"));
        }
        await db.SaveChangesAsync();
        var before = await ReadRowsAsync(db);
        if (prior is not null)
        {
            (await HashIndexesAsync(db)).Should().HaveCount(5);
            await db.Database.MigrateAsync();
        }
        db.ChangeTracker.Clear();
        (await ReadRowsAsync(db)).Should().BeEquivalentTo(before, options => options.WithStrictOrdering(), "all original IDs, timestamps, JSON/hash bytes and sources must survive");
        (await HashIndexesAsync(db)).Should().BeEmpty();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        foreach (var table in Tables)
        {
            db.Add(Seed(table, "source", At.AddSeconds(2), "a"));
            db.Add(Seed(table, "other-source", At.AddSeconds(2), "a"));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rows = await ReadRowsAsync(db);
        rows.Should().HaveCount(20);
        rows.Where(row => row.Source == "source" && row.Hash == "a").Should().HaveCount(10, "A can recur at a new observation time in every table");

        foreach (var table in Tables)
        {
            db.Add(Seed(table, "source", At.AddSeconds(2), "a"));
            var save = () => db.SaveChangesAsync();
            var failure = await save.Should().ThrowAsync<DbUpdateException>();
            failure.Which.InnerException.Should().BeOfType<SqlException>().Which.Number.Should().BeOneOf(2601, 2627);
            db.ChangeTracker.Clear();
        }
        (await ReadRowsAsync(db)).Should().BeEquivalentTo(rows, options => options.WithStrictOrdering());
    }

    [TestMethod]
    public async Task DownBeforeNewRepeatedContentRestoresIndexes_AfterRepeatsFailsWithoutDeletingRows()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        foreach (var table in Tables) db.Add(Seed(table, "source", At, "a"));
        await db.SaveChangesAsync();
        var before = await ReadRowsAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(Prior);
        (await HashIndexesAsync(db)).Should().HaveCount(5);
        (await ReadRowsAsync(db)).Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
        await db.Database.MigrateAsync();
        foreach (var table in Tables) db.Add(Seed(table, "source", At.AddSeconds(1), "a"));
        await db.SaveChangesAsync();
        var repeated = await ReadRowsAsync(db);
        var downgrade = () => db.GetService<IMigrator>().MigrateAsync(Prior);
        (await downgrade.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(51000);
        (await ReadRowsAsync(db)).Should().BeEquivalentTo(repeated, options => options.WithStrictOrdering());
        (await HashIndexesAsync(db)).Should().BeEmpty();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    private static object Seed(string table, string source, DateTime at, string hash) => table switch
    {
        "PowerDeviceInventorySnapshot" => new PowerDeviceInventorySnapshot { SourceId = source, RecordedAt = at, CreatedAt = At, PayloadHash = hash, PayloadJson = JsonSerializer.Serialize(new { State = hash }) },
        "PowerConfigurationSnapshot" => new PowerConfigurationSnapshot { SourceId = source, RecordedAt = at, CreatedAt = At, PayloadHash = hash, PayloadJson = JsonSerializer.Serialize(new { State = hash }) },
        "PowerEnergySnapshot" => new PowerEnergySnapshot { SourceId = source, RecordedAt = at, CreatedAt = At, PayloadHash = hash, PayloadJson = JsonSerializer.Serialize(new { State = hash }) },
        "PowerInverterDetailSnapshot" => new PowerInverterDetailSnapshot { SourceId = source, RecordedAt = at, CreatedAt = At, PayloadHash = hash, PayloadJson = JsonSerializer.Serialize(new { State = hash }) },
        "GatewayStatusSnapshot" => new GatewayStatusSnapshot { SourceId = source, RecordedAt = at, CreatedAt = At, PayloadHash = hash, PayloadJson = JsonSerializer.Serialize(new { State = hash }), GatewayId = "gateway", HealthState = "Healthy", SourceFreshnessState = "Live", RestState = "Live" },
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private sealed record Row(string Table, long Id, string Source, DateTime At, DateTime Created, string Hash, string Json);
    private static async Task<List<Row>> ReadRowsAsync(HvoV9DbContext db)
    {
        await db.Database.OpenConnectionAsync();
        var rows = new List<Row>();
        foreach (var table in Tables)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT Id, SourceId, RecordedAt, CreatedAt, PayloadHash, PayloadJson FROM v9.[{table}] ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(new(table, reader.GetInt64(0), reader.GetString(1), reader.GetDateTime(2), reader.GetDateTime(3), reader.GetString(4), reader.GetString(5)));
        }
        return rows;
    }
    private static async Task<List<string>> HashIndexesAsync(HvoV9DbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sys.indexes WHERE name LIKE 'IX%SourceId_PayloadHash' AND is_unique=1";
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new List<string>();
        while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
        return indexes;
    }
}

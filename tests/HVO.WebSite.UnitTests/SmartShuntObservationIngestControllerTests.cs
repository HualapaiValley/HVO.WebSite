using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class SmartShuntObservationIngestControllerTests
{
    [TestMethod]
    public async Task IngestBatch_RetryingExecutionStrategy_AtomicallyPersistsSummaryAndDetail()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var setupOptions = new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options;
        await using (var setup = new HvoV9DbContext(setupOptions))
            await setup.Database.EnsureCreatedAsync();

        var options = new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseSqlite(connection)
            .ReplaceService<IExecutionStrategyFactory, RetryingExecutionStrategyFactory>()
            .Options;
        await using var db = new HvoV9DbContext(options);
        var recordedAt = new DateTime(2026, 8, 12, 17, 30, 0, DateTimeKind.Utc);
        var controller = CreateController(db);

        var response = await controller.IngestBatch(
            Batch(recordedAt),
            CancellationToken.None);

        response.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        (await db.PowerReadings.CountAsync(row => row.SourceId == "smartshunt-test")).Should().Be(1);
        (await db.SmartShuntDetailSnapshots.CountAsync(row => row.SourceId == "smartshunt-test")).Should().Be(1);
    }

    [TestMethod]
    public async Task IngestBatch_WhenDetailInsertFails_RollsBackSummaryInsert()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options;
        await using var db = new HvoV9DbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_smartshunt_detail BEFORE INSERT ON SmartShuntDetailSnapshot BEGIN SELECT RAISE(ABORT, 'detail rejected'); END;");
        var controller = CreateController(db);

        var response = await controller.IngestBatch(
            Batch(new DateTime(2026, 8, 12, 17, 31, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        response.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        await using var verification = new HvoV9DbContext(options);
        (await verification.PowerReadings.CountAsync(row => row.SourceId == "smartshunt-test")).Should().Be(0);
        (await verification.SmartShuntDetailSnapshots.CountAsync(row => row.SourceId == "smartshunt-test")).Should().Be(0);
    }

    [TestMethod]
    public async Task IngestBatch_WhenCompetingRequestWins_ReconcilesAsSkippedAndContextRemainsUsable()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var setupOptions = new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options;
        await using (var setup = new HvoV9DbContext(setupOptions))
            await setup.Database.EnsureCreatedAsync();
        var recordedAt = new DateTime(2026, 8, 12, 17, 32, 0, DateTimeKind.Utc);
        var interceptor = new CompetingInsertInterceptor(async cancellationToken =>
        {
            await using var competing = new HvoV9DbContext(setupOptions);
            var payload = Payload(recordedAt);
            competing.PowerReadings.Add(new()
            {
                SourceId = payload.Summary.SourceId!, SourceSystem = payload.Summary.SourceSystem, DeviceId = payload.Summary.DeviceId,
                RecordedAt = recordedAt, BatteryVoltageV = payload.Summary.BatteryVoltageV, BatteryCurrentA = payload.Summary.BatteryCurrentA,
                BatteryPowerW = payload.Summary.BatteryPowerW, BatteryStateOfChargePercent = payload.Summary.BatteryStateOfChargePercent
            });
            competing.SmartShuntDetailSnapshots.Add(new()
            {
                SourceId = payload.Detail.SourceId!, SourceSystem = payload.Detail.SourceSystem, DeviceId = payload.Detail.DeviceId,
                RecordedAt = recordedAt, ConsumedAh = payload.Detail.ConsumedAh, RemainingMinutes = payload.Detail.RemainingMinutes,
                CreatedAt = DateTime.UtcNow
            });
            await competing.SaveChangesAsync(cancellationToken);
        });
        var options = new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new HvoV9DbContext(options);
        var controller = CreateController(db);

        var response = await controller.IngestBatch(Batch(recordedAt), CancellationToken.None);

        var result = response.Result.Should().BeOfType<ObjectResult>().Which;
        result.StatusCode.Should().Be(StatusCodes.Status201Created);
        var body = result.Value.Should().BeOfType<PowerReadingBatchResponse>().Which;
        body.Inserted.Should().Be(0);
        body.Skipped.Should().Be(1);
        (await db.PowerReadings.CountAsync(row => row.SourceId == "smartshunt-test")).Should().Be(1);
        (await db.SmartShuntDetailSnapshots.CountAsync(row => row.SourceId == "smartshunt-test")).Should().Be(1);
    }

    private static SmartShuntObservationIngestController CreateController(HvoV9DbContext db)
    {
        var controller = new SmartShuntObservationIngestController(
            db,
            NullLogger<SmartShuntObservationIngestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("source", "smartshunt-test")],
            "test"));
        return controller;
    }

    private static JsonElement Batch(DateTime recordedAt) =>
        JsonSerializer.SerializeToElement(new[] { Payload(recordedAt) }, JsonSerializerOptions.Web);

    private static SmartShuntObservationPayload Payload(DateTime recordedAt) => new(
        new PowerReadingPayload
        {
            SourceId = "smartshunt-test", SourceSystem = "victron-smartshunt", DeviceId = "battery", RecordedAtUtc = recordedAt,
            BatteryVoltageV = 52, BatteryCurrentA = -5, BatteryPowerW = -260, BatteryStateOfChargePercent = 80
        },
        new SmartShuntDetailPayload
        {
            SourceId = "smartshunt-test", SourceSystem = "victron-smartshunt", DeviceId = "battery", RecordedAtUtc = recordedAt,
            ConsumedAh = -20, RemainingMinutes = 90
        });

    private sealed class RetryingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryingExecutionStrategy(dependencies);
    }

    private sealed class RetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private sealed class CompetingInsertInterceptor(Func<CancellationToken, Task> insert) : SaveChangesInterceptor
    {
        private bool inserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!inserted)
            {
                inserted = true;
                await insert(cancellationToken);
            }
            return result;
        }
    }
}

using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Security.Claims;
using HVO.WebSite.v9.Infrastructure;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class WeatherV9BatchControllerTests
{
    private static HvoV9DbContext CreateDb() =>
        new(new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"V9Batch_{Guid.NewGuid()}")
            .Options);

    private static WeatherIngestController CreateController(HvoV9DbContext db)
    {
        var ctrl = new WeatherIngestController(db, NullLogger<WeatherIngestController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        ctrl.ProblemDetailsFactory = new DefaultProblemDetailsFactory(
            Options.Create(new ApiBehaviorOptions()));
        return ctrl;
    }

    private static JsonElement ToJsonElement<T>(IReadOnlyList<T> requests)
    {
        var json = JsonSerializer.Serialize(requests, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // -------------------------------------------------------------------------
    // Empty batch
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRawBatch_EmptyList_Returns400()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var result = await ctrl.IngestRawBatch(ToJsonElement(new List<IngestWeatherRawRequest> {  }), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // -------------------------------------------------------------------------
    // Happy path — all new records
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRawBatch_AllNew_InsertsAndReturns201()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var batch = new[]
        {
            MakeRequest("2026-04-29T01:00:00Z", 68.1),
            MakeRequest("2026-04-29T01:05:00Z", 68.5),
            MakeRequest("2026-04-29T01:10:00Z", 68.9),
        };

        var result = await ctrl.IngestRawBatch(ToJsonElement(batch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<WeatherRawBatchResponse>().Subject;
        body.Inserted.Should().Be(3);
        body.Skipped.Should().Be(0);
        body.Failed.Should().BeEmpty();

        db.WeatherRaw.Count().Should().Be(3);
    }

    [TestMethod]
    public async Task IngestRawBatch_HomeAssistantSourceRequiresExactSourceClaim()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);
        var request = new IngestWeatherRawRequest
        {
            StationId = "govee:sensor-1",
            SourceSystem = null,
            RecordedAt = DateTime.Parse("2026-04-29T01:00:00Z").ToUniversalTime(),
            TemperatureF = 68,
            HumidityPercent = 40
        };

        var denied = await ctrl.IngestRawBatch(ToJsonElement(new[] { request }), CancellationToken.None);
        denied.Result.Should().BeOfType<ForbidResult>();

        ctrl.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(IngestSourceAuthority.SourceClaimType, "govee:sensor-1")
        ], "test"));
        var accepted = await ctrl.IngestRawBatch(ToJsonElement(new[] { request }), CancellationToken.None);
        accepted.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    // -------------------------------------------------------------------------
    // Idempotency — duplicate records are skipped, not duplicated in DB
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRawBatch_DuplicateRecords_SkipsExistingAndReturns201()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var batch = new[]
        {
            MakeRequest("2026-04-29T02:00:00Z", 70.0),
            MakeRequest("2026-04-29T02:05:00Z", 70.5),
        };

        // First call — both records inserted
        await ctrl.IngestRawBatch(ToJsonElement(batch), CancellationToken.None);

        // Second call with same records (retry scenario)
        var result = await ctrl.IngestRawBatch(ToJsonElement(batch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<WeatherRawBatchResponse>().Subject;
        body.Inserted.Should().Be(0);
        body.Skipped.Should().Be(2);
        body.Failed.Should().BeEmpty();

        // Still only 2 rows — no duplicates
        db.WeatherRaw.Count().Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Partial retry — some already inserted, some new
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRawBatch_PartialRetry_InsertsOnlyNewRecords()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var firstBatch = new[]
        {
            MakeRequest("2026-04-29T03:00:00Z", 65.0),
            MakeRequest("2026-04-29T03:05:00Z", 65.5),
            MakeRequest("2026-04-29T03:10:00Z", 66.0),
        };
        await ctrl.IngestRawBatch(ToJsonElement(firstBatch), CancellationToken.None);

        // Simulate a retry that re-sends already-inserted records plus new ones
        var retryBatch = new[]
        {
            MakeRequest("2026-04-29T03:05:00Z", 65.5),  // already inserted
            MakeRequest("2026-04-29T03:10:00Z", 66.0),  // already inserted
            MakeRequest("2026-04-29T03:15:00Z", 66.5),  // new
            MakeRequest("2026-04-29T03:20:00Z", 67.0),  // new
        };

        var result = await ctrl.IngestRawBatch(ToJsonElement(retryBatch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<WeatherRawBatchResponse>().Subject;
        body.Inserted.Should().Be(2);   // only the new ones
        body.Skipped.Should().Be(2);    // the previously-inserted ones
        body.Failed.Should().BeEmpty();

        // 3 original + 2 new = 5
        db.WeatherRaw.Count().Should().Be(5);
    }

    // -------------------------------------------------------------------------
    // Validation failure — bad record is dead-lettered, rest succeed
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRawBatch_OneInvalidRecord_DeadLettersItAndInsertsRest()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var batch = new[]
        {
            MakeRequest("2026-04-29T04:00:00Z", 68.0),
            // HumidityPercent = 150 violates [Range(0,100)]
            new IngestWeatherRawRequest
            {
                StationId = "hvo-davis-01",
                RecordedAt = DateTime.Parse("2026-04-29T04:05:00Z"),
                TemperatureF = 68.0,
                HumidityPercent = 150.0,  // invalid
            },
            MakeRequest("2026-04-29T04:10:00Z", 69.0),
        };

        var result = await ctrl.IngestRawBatch(ToJsonElement(batch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<WeatherRawBatchResponse>().Subject;
        body.Inserted.Should().Be(2);
        body.Skipped.Should().Be(0);
        body.Failed.Should().HaveCount(1);
        body.Failed[0].RecordedAt.Should().Be(DateTime.Parse("2026-04-29T04:05:00Z").ToUniversalTime());
        body.Failed[0].Error.Should().NotBeNullOrEmpty();

        // Only 2 valid records in DB
        db.WeatherRaw.Count().Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Single record (same path as batch of 1)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRawBatch_SingleRecord_Returns201WithInsertedOne()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);

        var batch = new[] { MakeRequest("2026-04-29T05:00:00Z", 72.3) };

        var result = await ctrl.IngestRawBatch(ToJsonElement(batch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<WeatherRawBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Skipped.Should().Be(0);
        body.Failed.Should().BeEmpty();
    }

    [TestMethod]
    public async Task IngestRawBatch_DavisLoopPayloadAliases_PersistsWindGustOnly()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);
        var json = """
        [
          {
            "StationId": "hvo-davis-01",
            "RecordedAt": "2026-04-29T06:00:00Z",
            "TemperatureF": 68.7,
            "HumidityPercent": 43.0,
            "BarometricPressureInHg": 29.91,
            "WindSpeedMph": 7.4,
            "WindDirectionDegrees": 225,
            "WindGust10MinMph": 18.6,
            "RainRateInchesPerHour": 0.12,
            "DailyRainInches": 0.31,
            "StormRainInches": 0.44,
            "SolarRadiationWm2": 742.0,
            "UvIndex": 4.1
          }
        ]
        """;
        var batch = JsonSerializer.Deserialize<List<IngestWeatherRawRequest>>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var result = await ctrl.IngestRawBatch(ToJsonElement(batch), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<WeatherRawBatchResponse>().Subject;
        body.Inserted.Should().Be(1);
        body.Failed.Should().BeEmpty();

        var saved = db.WeatherRaw.Single();
        saved.WindGustMph.Should().Be(18.6);
        saved.RainfallInches.Should().BeNull();
        saved.SolarRadiationWm2.Should().Be(742.0);
        saved.UvIndex.Should().Be(4.1);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IngestWeatherRawRequest MakeRequest(string recordedAt, double tempF) => new()
    {
        StationId = "hvo-davis-01",
        RecordedAt = DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
        TemperatureF = tempF,
        HumidityPercent = 40.0,
        BarometricPressureInHg = 29.92,
        WindSpeedMph = 5.0,
        WindDirectionDegrees = 180
    };
}

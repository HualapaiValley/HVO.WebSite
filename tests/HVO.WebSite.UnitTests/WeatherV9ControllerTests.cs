using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class WeatherV9ControllerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HvoV9DbContext CreateDbContext(string name) =>
        new(new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static WeatherIngestController CreateController(HvoV9DbContext db) =>
        new(db, NullLogger<WeatherIngestController>.Instance);

    private static WeatherRaw MakeRaw(string stationId, DateTime recordedAt, double tempF = 70.0) =>
        new()
        {
            StationId = stationId,
            RecordedAt = recordedAt,
            TemperatureF = tempF
        };

    private static WeatherHourly MakeHourly(string stationId, DateTime periodStart) =>
        new()
        {
            StationId = stationId,
            PeriodStart = periodStart,
            AvgTemperatureF = 68.0
        };

    // -------------------------------------------------------------------------
    // IngestRaw
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRaw_ReturnsCreated_AndPersistsRecord()
    {
        await using var db = CreateDbContext(nameof(IngestRaw_ReturnsCreated_AndPersistsRecord));
        var controller = CreateController(db);

        var request = new IngestWeatherRawRequest
        {
            StationId = "hvo-davis-01",
            RecordedAt = new DateTime(2026, 4, 27, 3, 0, 0, DateTimeKind.Utc),
            TemperatureF = 68.5,
            HumidityPercent = 42.0
        };

        var action = await controller.IngestRaw(request, CancellationToken.None);

        var created = action.Result as CreatedAtActionResult;
        created.Should().NotBeNull();
        created!.StatusCode.Should().Be(StatusCodes.Status201Created);

        var payload = created.Value as WeatherRawResponse;
        payload.Should().NotBeNull();
        payload!.StationId.Should().Be("hvo-davis-01");
        payload.TemperatureF.Should().Be(68.5);
        payload.HumidityPercent.Should().Be(42.0);
        payload.Id.Should().BeGreaterThan(0);

        var saved = await db.WeatherRaw.SingleAsync();
        saved.StationId.Should().Be("hvo-davis-01");
        saved.TemperatureF.Should().Be(68.5);
    }

    [TestMethod]
    public async Task IngestRaw_UsesUtcNow_WhenRecordedAtNotProvided()
    {
        await using var db = CreateDbContext(nameof(IngestRaw_UsesUtcNow_WhenRecordedAtNotProvided));
        var controller = CreateController(db);
        var before = DateTime.UtcNow;

        var request = new IngestWeatherRawRequest
        {
            StationId = "hvo-davis-01",
            RecordedAt = null
        };

        await controller.IngestRaw(request, CancellationToken.None);

        var saved = await db.WeatherRaw.SingleAsync();
        saved.RecordedAt.Should().BeOnOrAfter(before);
        saved.RecordedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task IngestRaw_NormalizesRecordedAt_ToUtc()
    {
        await using var db = CreateDbContext(nameof(IngestRaw_NormalizesRecordedAt_ToUtc));
        var controller = CreateController(db);
        var localTime = new DateTime(2026, 4, 27, 3, 0, 0, DateTimeKind.Utc);

        var request = new IngestWeatherRawRequest
        {
            StationId = "hvo-davis-01",
            RecordedAt = localTime
        };

        await controller.IngestRaw(request, CancellationToken.None);

        var saved = await db.WeatherRaw.SingleAsync();
        saved.RecordedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    // -------------------------------------------------------------------------
    // GetRecentRaw
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetRecentRaw_ReturnsEmptyList_WhenDatabaseIsEmpty()
    {
        await using var db = CreateDbContext(nameof(GetRecentRaw_ReturnsEmptyList_WhenDatabaseIsEmpty));
        var controller = CreateController(db);

        var action = await controller.GetRecentRaw(null, 100, CancellationToken.None);

        var ok = action.Result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.StatusCode.Should().Be(StatusCodes.Status200OK);

        var list = ok.Value as IReadOnlyList<WeatherRawResponse>;
        list.Should().NotBeNull().And.BeEmpty();
    }

    [TestMethod]
    public async Task GetRecentRaw_ReturnsRecords_OrderedNewestFirst()
    {
        await using var db = CreateDbContext(nameof(GetRecentRaw_ReturnsRecords_OrderedNewestFirst));
        var now = DateTime.UtcNow;
        db.WeatherRaw.AddRange(
            MakeRaw("hvo-01", now.AddMinutes(-10)),
            MakeRaw("hvo-01", now.AddMinutes(-5)),
            MakeRaw("hvo-01", now.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetRecentRaw(null, 100, CancellationToken.None);

        var list = (action.Result as OkObjectResult)!.Value as IReadOnlyList<WeatherRawResponse>;
        list.Should().HaveCount(3);
        list![0].RecordedAt.Should().BeAfter(list[1].RecordedAt);
        list[1].RecordedAt.Should().BeAfter(list[2].RecordedAt);
    }

    [TestMethod]
    public async Task GetRecentRaw_FiltersByStationId()
    {
        await using var db = CreateDbContext(nameof(GetRecentRaw_FiltersByStationId));
        var now = DateTime.UtcNow;
        db.WeatherRaw.AddRange(
            MakeRaw("hvo-01", now.AddMinutes(-2)),
            MakeRaw("hvo-02", now.AddMinutes(-1)),
            MakeRaw("hvo-01", now));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetRecentRaw("hvo-01", 100, CancellationToken.None);

        var list = (action.Result as OkObjectResult)!.Value as IReadOnlyList<WeatherRawResponse>;
        list.Should().HaveCount(2);
        list!.Should().AllSatisfy(r => r.StationId.Should().Be("hvo-01"));
    }

    [TestMethod]
    public async Task GetRecentRaw_RespectsLimit()
    {
        await using var db = CreateDbContext(nameof(GetRecentRaw_RespectsLimit));
        var now = DateTime.UtcNow;
        db.WeatherRaw.AddRange(Enumerable.Range(0, 10)
            .Select(i => MakeRaw("hvo-01", now.AddMinutes(-i))));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetRecentRaw(null, 3, CancellationToken.None);

        var list = (action.Result as OkObjectResult)!.Value as IReadOnlyList<WeatherRawResponse>;
        list.Should().HaveCount(3);
    }

    // -------------------------------------------------------------------------
    // GetRecentHourly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetRecentHourly_ReturnsRecords_OrderedNewestFirst()
    {
        await using var db = CreateDbContext(nameof(GetRecentHourly_ReturnsRecords_OrderedNewestFirst));
        var now = DateTime.UtcNow;
        db.WeatherHourly.AddRange(
            MakeHourly("hvo-01", now.AddHours(-3)),
            MakeHourly("hvo-01", now.AddHours(-2)),
            MakeHourly("hvo-01", now.AddHours(-1)));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetRecentHourly(null, 24, CancellationToken.None);

        var ok = action.Result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.StatusCode.Should().Be(StatusCodes.Status200OK);

        var list = ok.Value as IReadOnlyList<WeatherHourlyResponse>;
        list.Should().HaveCount(3);
        list![0].PeriodStart.Should().BeAfter(list[1].PeriodStart);
    }

    [TestMethod]
    public async Task GetRecentHourly_FiltersByStationId()
    {
        await using var db = CreateDbContext(nameof(GetRecentHourly_FiltersByStationId));
        var now = DateTime.UtcNow;
        db.WeatherHourly.AddRange(
            MakeHourly("hvo-01", now.AddHours(-2)),
            MakeHourly("hvo-02", now.AddHours(-1)),
            MakeHourly("hvo-01", now));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetRecentHourly("hvo-02", 24, CancellationToken.None);

        var list = (action.Result as OkObjectResult)!.Value as IReadOnlyList<WeatherHourlyResponse>;
        list.Should().HaveCount(1);
        list![0].StationId.Should().Be("hvo-02");
    }

    [TestMethod]
    public async Task GetRecentHourly_RespectsLimit()
    {
        await using var db = CreateDbContext(nameof(GetRecentHourly_RespectsLimit));
        var now = DateTime.UtcNow;
        db.WeatherHourly.AddRange(Enumerable.Range(0, 24)
            .Select(i => MakeHourly("hvo-01", now.AddHours(-i))));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetRecentHourly(null, 6, CancellationToken.None);

        var list = (action.Result as OkObjectResult)!.Value as IReadOnlyList<WeatherHourlyResponse>;
        list.Should().HaveCount(6);
    }

    [TestMethod]
    public async Task GetRecentHourly_MapsAllFields_Correctly()
    {
        await using var db = CreateDbContext(nameof(GetRecentHourly_MapsAllFields_Correctly));
        var now = new DateTime(2026, 4, 27, 12, 0, 0, DateTimeKind.Utc);
        db.WeatherHourly.Add(new WeatherHourly
        {
            StationId = "hvo-01",
            PeriodStart = now,
            AvgTemperatureF = 68.4,
            MinTemperatureF = 64.1,
            MaxTemperatureF = 72.8,
            AvgHumidityPercent = 35.0,
            AvgWindSpeedMph = 7.2,
            MaxWindGustMph = 14.0,
            DominantWindDirectionDegrees = 270,
            TotalRainfallInches = 0.0,
            AvgSolarRadiationWm2 = 512.0,
            MaxUvIndex = 3.2
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetRecentHourly(null, 1, CancellationToken.None);

        var record = ((action.Result as OkObjectResult)!.Value as IReadOnlyList<WeatherHourlyResponse>)![0];
        record.AvgTemperatureF.Should().Be(68.4);
        record.MinTemperatureF.Should().Be(64.1);
        record.MaxTemperatureF.Should().Be(72.8);
        record.MaxWindGustMph.Should().Be(14.0);
        record.DominantWindDirectionDegrees.Should().Be(270);
        record.AvgSolarRadiationWm2.Should().Be(512.0);
    }
}

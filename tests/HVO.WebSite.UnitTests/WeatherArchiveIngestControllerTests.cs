using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.Edge.Contracts.Weather;
using HVO.WebSite.v9.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class WeatherArchiveIngestControllerTests
{
    [TestMethod]
    public async Task IngestBatch_PersistsCompleteArchiveAndIsIdempotent()
    {
        await using var db = new HvoV9DbContext(new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"weather-archive-{Guid.NewGuid():N}").Options);
        var controller = new WeatherArchiveIngestController(db, NullLogger<WeatherArchiveIngestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var payload = CompletePayload();

        var first = await controller.IngestBatch([payload], CancellationToken.None);
        var second = await controller.IngestBatch([payload], CancellationToken.None);

        ((ObjectResult)first.Result!).StatusCode.Should().Be(StatusCodes.Status201Created);
        ((WeatherArchiveBatchResponse)((ObjectResult)first.Result!).Value!).Inserted.Should().Be(1);
        var retry = (WeatherArchiveBatchResponse)((ObjectResult)second.Result!).Value!;
        retry.Inserted.Should().Be(0);
        retry.Skipped.Should().Be(1);
        var saved = await db.WeatherArchive.SingleAsync();
        saved.DownloadRecordType.Should().Be(1);
        saved.WindGustDirectionDegrees.Should().Be(202.5);
        JsonSerializer.Deserialize<double?[]>(saved.LeafWetnessJson).Should().Equal(1, null);
        JsonSerializer.Deserialize<double?[]>(saved.SoilTemperaturesJson).Should().Equal(60, 61, null, 63);
        JsonSerializer.Deserialize<double?[]>(saved.ExtraHumiditiesJson).Should().Equal(40, null);
        JsonSerializer.Deserialize<double?[]>(saved.ExtraTemperaturesJson).Should().Equal(64, null, 66);
        JsonSerializer.Deserialize<double?[]>(saved.SoilMoisturesJson).Should().Equal(10, 20, null, 40);
    }

    [TestMethod]
    public async Task IngestBatch_AccountsForNullAndOversizedForecastEntriesWithoutThrowing()
    {
        await using var db = new HvoV9DbContext(new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"weather-archive-invalid-{Guid.NewGuid():N}").Options);
        var controller = new WeatherArchiveIngestController(db, NullLogger<WeatherArchiveIngestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var oversized = CompletePayload() with { ForecastString = new string('x', 513) };

        var result = await controller.IngestBatch([null!, oversized], CancellationToken.None);

        var response = ((ObjectResult)result.Result!).Value.Should()
            .BeOfType<WeatherArchiveBatchResponse>().Subject;
        response.Inserted.Should().Be(0);
        response.Skipped.Should().Be(0);
        response.Failed.Should().HaveCount(2);
        db.WeatherArchive.Should().BeEmpty();
    }

    private static DavisWeatherArchivePayload CompletePayload() => new()
    {
        StationId = "hvo-davis-01",
        RecordedAtUtc = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
        ConsoleRecordedAtLocal = new DateTime(2026, 8, 11, 5, 0, 0, DateTimeKind.Unspecified),
        ArchiveIntervalMinutes = 5, TemperatureF = 70, HighTemperatureF = 72, LowTemperatureF = 68,
        InsideTemperatureF = 75, HumidityPercent = 30, InsideHumidityPercent = 35,
        BarometricPressureInHg = 29.9, WindSpeedMph = 4, WindGustMph = 9,
        WindDirectionDegrees = 180, WindGustDirectionDegrees = 202.5, WindSamples = 150,
        RainfallInches = .01, RainRateInchesPerHour = .1, SolarRadiationWm2 = 600,
        HighSolarRadiationWm2 = 700, UvIndex = 4, HighUvIndex = 5, EtInches = .002,
        ForecastRule = 6, ForecastString = "Mostly clear", DownloadRecordType = 1,
        LeafTemp1F = 65, LeafTemp2F = 66, LeafWetnessScaled = [1, null],
        SoilTemperaturesF = [60, 61, null, 63], ExtraHumiditiesPercent = [40, null],
        ExtraTemperaturesF = [64, null, 66], SoilMoisturesCb = [10, 20, null, 40],
    };
}

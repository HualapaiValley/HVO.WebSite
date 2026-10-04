using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
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
public sealed class CanonicalIngestRetryTests
{
    private static readonly DateTime At = new(2026, 8, 12, 17, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PowerOverlap_AcknowledgesOnlyDurableIdentitiesAndRetryRecovers(bool allCommitted)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var race = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var competitor = database.CreateContext();
            competitor.PowerReadings.Add(new PowerReading { SourceId = "power-retry", RecordedAt = At });
            if (allCommitted)
                competitor.PowerReadings.Add(new PowerReading { SourceId = "power-retry", RecordedAt = At.AddMinutes(1) });
            await competitor.SaveChangesAsync(ct);
        });
        await using var db = database.CreateContext(race);
        using var telemetry = new PowerIngestTelemetry();
        var service = new PowerReadingIngestService(db, telemetry, NullLogger<PowerReadingIngestService>.Instance);
        PowerReadingPayload[] batch =
        [
            new() { SourceId = "power-retry", RecordedAtUtc = At },
            new() { SourceId = "power-retry", RecordedAtUtc = At.AddMinutes(1) },
            new() { SourceId = "power-retry", RecordedAtUtc = At.AddMinutes(1) }
        ];

        var response = await service.IngestReadingsAsync(batch, CancellationToken.None);
        response.PersistenceFailed.Should().Be(!allCommitted);
        if (allCommitted)
        {
            response.Response.Inserted.Should().Be(0);
            response.Response.Skipped.Should().Be(3);
        }
        db.ChangeTracker.Entries().Should().NotContain(entry => entry.State == EntityState.Added);
        var retry = await service.IngestReadingsAsync(batch, CancellationToken.None);
        retry.PersistenceFailed.Should().BeFalse();
        (retry.Response.Inserted + retry.Response.Skipped).Should().Be(3);
        retry.Response.Failed.Should().BeEmpty();
        await using var verification = database.CreateContext();
        (await verification.PowerReadings.CountAsync()).Should().Be(2);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WeatherOverlap_ReturnsRetryableFailureUntilEveryIdentityIsDurable(bool allCommitted)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var race = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var competitor = database.CreateContext();
            competitor.WeatherRaw.Add(new WeatherRaw { StationId = "weather-retry", RecordedAt = At });
            if (allCommitted)
                competitor.WeatherRaw.Add(new WeatherRaw { StationId = "weather-retry", RecordedAt = At.AddMinutes(1) });
            await competitor.SaveChangesAsync(ct);
        });
        await using var db = database.CreateContext(race);
        var controller = new WeatherIngestController(db, NullLogger<WeatherIngestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new DefaultProblemDetailsFactory(Options.Create(new ApiBehaviorOptions()))
        };
        IngestWeatherRawRequest[] requests =
        [
            new() { StationId = "weather-retry", RecordedAt = At },
            new() { StationId = "weather-retry", RecordedAt = At.AddMinutes(1) },
            new() { StationId = "weather-retry", RecordedAt = At.AddMinutes(1) }
        ];
        var batch = JsonSerializer.SerializeToElement(requests, JsonSerializerOptions.Web);
        var result = await controller.IngestRawBatch(batch, CancellationToken.None);
        ((ObjectResult)result.Result!).StatusCode.Should().Be(allCommitted ? 201 : 500);
        if (allCommitted)
            ((WeatherRawBatchResponse)((ObjectResult)result.Result!).Value!).Skipped.Should().Be(3);
        db.ChangeTracker.Entries().Should().NotContain(entry => entry.State == EntityState.Added);
        var retry = await controller.IngestRawBatch(batch, CancellationToken.None);
        var response = (WeatherRawBatchResponse)((ObjectResult)retry.Result!).Value!;
        (response.Inserted + response.Skipped).Should().Be(3);
        response.Failed.Should().BeEmpty();
        await using var verification = database.CreateContext();
        (await verification.WeatherRaw.CountAsync()).Should().Be(2);
    }
}

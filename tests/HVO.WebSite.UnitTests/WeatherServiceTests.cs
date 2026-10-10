using FluentAssertions;
using HVO.Core.Results;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class WeatherServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 21, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Current_UsesLatestLiveReading_WithoutLegacyTables()
    {
        var latest = new WeatherRaw
        {
            StationId = "davis", RecordedAt = Now.UtcDateTime,
            TemperatureF = 84.7, HumidityPercent = 26, DewPointF = 46,
            BarometricPressureInHg = 29.416, WindSpeedMph = 15, WindDirectionDegrees = 190,
            SolarRadiationWm2 = 592, UvIndex = 3.5, RainfallInches = 0.25
        };
        await using var store = await TestStore.CreateAsync(latest, Reading(Now.AddMinutes(-1), 50));

        var response = Success(await store.Service.GetCurrentWeatherConditionsAsync());

        response.Timestamp.Should().Be(Now.UtcDateTime);
        response.Current.RecordDateTime.Should().Be(Now);
        response.Current.RecordDateTime!.Value.Offset.Should().Be(TimeSpan.Zero);
        response.Current.OutsideTemperature.Should().Be(84.7m);
        response.Current.OutsideHumidity.Should().Be(26);
        response.Current.OutsideDewpoint.Should().Be(46);
        response.Current.Barometer.Should().Be(29.416m);
        response.Current.WindSpeed.Should().Be(15);
        response.Current.WindDirection.Should().Be(190);
        response.Current.SolarRadiation.Should().Be(592);
        response.Current.UvIndex.Should().Be(3.5m);
        // Raw rainfall is not a daily accumulation. Missing live columns stay unknown.
        response.Current.DailyRainAmount.Should().BeNull();
        response.Current.InsideTemperature.Should().BeNull();
        response.Current.InsideHumidity.Should().BeNull();
        response.Current.BarometerTrend.Should().BeNull();
        response.Current.RainRate.Should().BeNull();
        response.Current.MonthlyRainAmount.Should().BeNull();
        response.Current.YearlyRainAmount.Should().BeNull();
        response.Current.OutsideHeatIndex.Should().BeNull();
        response.Current.OutsideWindChill.Should().BeNull();
        response.Current.SunriseTime.Should().BeNull();
        response.Current.SunsetTime.Should().BeNull();
        store.Live.ChangeTracker.Entries().Should().BeEmpty();
    }

    [TestMethod]
    public async Task Current_ExtremesIgnoreNullsAndOtherStations_AndRetainEarliestTiedTime()
    {
        var lowTime = Now.AddHours(-3);
        var highTime = Now.AddHours(-2);
        var low = Reading(lowTime, 60);
        low.HumidityPercent = 20; low.WindSpeedMph = 0; low.WindDirectionDegrees = 90;
        low.BarometricPressureInHg = 29; low.DewPointF = 30; low.SolarRadiationWm2 = 0; low.UvIndex = 0;
        var high = Reading(highTime, 90);
        high.HumidityPercent = 60; high.WindSpeedMph = 25; high.WindDirectionDegrees = 270;
        high.BarometricPressureInHg = 30; high.DewPointF = 50; high.SolarRadiationWm2 = 900; high.UvIndex = 5;
        var tied = Reading(highTime.AddMinutes(10), 90);
        await using var store = await TestStore.CreateAsync(
            low, high, tied, Reading(Now, null), Reading(Now.AddMinutes(-1), 150, "other-station"));

        var response = Success(await store.Service.GetCurrentWeatherConditionsAsync());

        response.Current.OutsideTemperature.Should().BeNull();
        var extremes = response.TodaysExtremes!;
        extremes.OutsideTemperature.High.Should().Be(90);
        extremes.OutsideTemperature.HighTime.Should().Be(highTime);
        extremes.OutsideTemperature.Low.Should().Be(60);
        extremes.OutsideTemperature.LowTime.Should().Be(lowTime);
        extremes.OutsideHumidity.High.Should().Be(60);
        extremes.OutsideHumidity.Low.Should().Be(20);
        extremes.WindSpeed.High.Should().Be(25);
        extremes.WindSpeed.HighTime.Should().Be(highTime);
        extremes.WindSpeed.HighDirection.Should().Be(270);
        extremes.WindSpeed.Low.Should().Be(0);
        extremes.WindSpeed.LowTime.Should().Be(lowTime);
        extremes.WindSpeed.LowDirection.Should().Be(90);
        extremes.Barometer.High.Should().Be(30);
        extremes.Barometer.Low.Should().Be(29);
        extremes.DewPoint.High.Should().Be(50);
        extremes.DewPoint.Low.Should().Be(30);
        extremes.SolarRadiation.High.Should().Be(900);
        extremes.SolarRadiation.HighTime.Should().Be(highTime);
        extremes.UvIndex.High.Should().Be(5);
        extremes.UvIndex.HighTime.Should().Be(highTime);
        extremes.InsideTemperature.High.Should().BeNull();
        extremes.InsideHumidity.High.Should().BeNull();
        extremes.HeatIndex.High.Should().BeNull();
        extremes.WindChill.High.Should().BeNull();
    }

    [TestMethod]
    public async Task Current_ExtremesUseServerLocalDay_WithUtcObservationTimes()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-arizona", TimeSpan.FromHours(-7), "Arizona", "Arizona");
        var start = new DateTimeOffset(2026, 10, 10, 7, 0, 0, TimeSpan.Zero);
        var nextDay = start.AddDays(1);
        await using var store = await TestStore.CreateAsync(
            Reading(start.AddTicks(-1), -10), Reading(start, 50), Reading(Now, 80), Reading(nextDay, 150));
        var service = store.CreateService(new FixedTimeProvider(Now, zone));

        var response = Success(await service.GetCurrentWeatherConditionsAsync());

        response.TodaysExtremes!.OutsideTemperature.Low.Should().Be(50);
        response.TodaysExtremes.OutsideTemperature.LowTime.Should().Be(start);
        response.TodaysExtremes.OutsideTemperature.High.Should().Be(80);
        response.TodaysExtremes.OutsideTemperature.HighTime.Should().Be(Now);
    }

    [TestMethod]
    public async Task Current_ReturnsLatestObservationWithoutTodaysExtremes_WhenOnlyOlderDataExists()
    {
        var yesterday = Now.AddDays(-1);
        await using var store = await TestStore.CreateAsync(Reading(yesterday, 70));

        var response = Success(await store.Service.GetCurrentWeatherConditionsAsync());

        response.Current.RecordDateTime.Should().Be(yesterday);
        response.TodaysExtremes.Should().BeNull();
    }

    [TestMethod]
    public async Task Current_PreservesUnknownMeasurements_WhenAllValuesAreNull()
    {
        await using var store = await TestStore.CreateAsync(Reading(Now, null));

        var response = Success(await store.Service.GetCurrentWeatherConditionsAsync());

        response.Current.OutsideTemperature.Should().BeNull();
        response.Current.Barometer.Should().BeNull();
        response.TodaysExtremes.Should().BeNull();
    }

    [TestMethod]
    public async Task Current_Returns404_WhenLiveTableHasNoObservations()
    {
        await using var store = await TestStore.CreateAsync();
        var controller = new WeatherController(store.Service, NullLogger<WeatherController>.Instance);

        var result = await controller.GetCurrentWeatherConditions();

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task Current_ReturnsSanitized500_WhenLiveQueryFails()
    {
        await using var store = await TestStore.CreateAsync();
        await store.Live.Database.ExecuteSqlRawAsync("DROP TABLE WeatherRaw");
        var controller = new WeatherController(store.Service, NullLogger<WeatherController>.Instance);

        var result = await controller.GetCurrentWeatherConditions();

        var error = result.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        error.Value.Should().BeOfType<ProblemDetails>().Which.Detail.Should()
            .Be("An error occurred while retrieving current weather conditions");
    }

    [TestMethod]
    public async Task Current_PropagatesRequestCancellation()
    {
        await using var store = await TestStore.CreateAsync(Reading(Now, 70));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => store.Service.GetCurrentWeatherConditionsAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task Current_ReadsDeployedSqlServerSchema()
    {
        var connectionString = Environment.GetEnvironmentVariable("HVO_WEATHER_LIVE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("Set HVO_WEATHER_LIVE_CONNECTION_STRING to verify the deployed weather schema.");

        await using var live = new HvoV9DbContext(
            new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlServer(connectionString).Options);
        await using var legacy = new HvoDbContext(
            new DbContextOptionsBuilder<HvoDbContext>().UseSqlServer(connectionString).Options);
        var service = new WeatherService(legacy, live, TimeProvider.System, NullLogger<WeatherService>.Instance);

        // This verification only issues SELECTs; it neither initializes nor migrates the database.
        var response = Success(await service.GetCurrentWeatherConditionsAsync());

        response.Current.RecordDateTime.Should().NotBeNull();
        response.Current.RecordDateTime!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    private static WeatherRaw Reading(DateTimeOffset at, double? temperature, string station = "davis") => new()
    {
        StationId = station, RecordedAt = at.UtcDateTime, TemperatureF = temperature
    };

    private static CurrentWeatherResponse Success(Result<CurrentWeatherResponse> result) =>
        result.Match(success: response => response,
            failure: error => throw new AssertFailedException(error?.ToString() ?? "Weather query failed without an error."));

    private sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo? zone = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => zone ?? TimeZoneInfo.Utc;
    }

    private sealed class TestStore(SqliteConnection connection, HvoV9DbContext live, HvoDbContext legacy) : IAsyncDisposable
    {
        public HvoV9DbContext Live => live;
        public WeatherService Service => CreateService(new FixedTimeProvider(Now));

        public WeatherService CreateService(TimeProvider timeProvider) =>
            new(legacy, live, timeProvider, NullLogger<WeatherService>.Instance);

        public static async Task<TestStore> CreateAsync(params WeatherRaw[] readings)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var live = new HvoV9DbContext(new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options);
            await live.Database.EnsureCreatedAsync();
            live.WeatherRaw.AddRange(readings);
            await live.SaveChangesAsync();
            live.ChangeTracker.Clear();
            // Only the v9 schema exists: any query against legacy weather tables fails.
            var legacy = new HvoDbContext(new DbContextOptionsBuilder<HvoDbContext>().UseSqlite(connection).Options);
            return new TestStore(connection, live, legacy);
        }

        public async ValueTask DisposeAsync()
        {
            await legacy.DisposeAsync();
            await live.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

using FluentAssertions;
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
public sealed class WeatherHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 19, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    [TestMethod]
    public void Range_DefaultsAndSingleDatesUseExactly24HoursInUtc()
    {
        var clock = new Clock();
        WeatherHistoryRange.Create(null, null, clock).Should().Be(new WeatherHistoryRange(Now.AddDays(-1), Now));
        var start = Now.AddDays(-10).ToOffset(TimeSpan.FromHours(-7));
        WeatherHistoryRange.Create(start, null, clock).Should().Be(new WeatherHistoryRange(start.ToUniversalTime(), start.ToUniversalTime().AddDays(1)));
        WeatherHistoryRange.Create(null, start, clock).Should().Be(new WeatherHistoryRange(start.ToUniversalTime().AddDays(-1), start.ToUniversalTime()));
    }

    [TestMethod]
    [DataRow("2026-10-10T19:00:00Z", "2026-10-11T19:00:00Z", true)]
    [DataRow("2026-10-10T12:00:00-07:00", "2026-10-11T19:00:00Z", true)]
    [DataRow("2026-10-10T19:00:00Z", "2026-10-11T19:00:00.0000001Z", false)]
    [DataRow("2026-10-11T19:00:00Z", "2026-10-11T19:00:00Z", false)]
    [DataRow("2026-10-11T19:00:00Z", "2026-10-10T19:00:00Z", false)]
    [DataRow("2026-10-11T12:00:00-07:00", "2026-10-11T19:00:01Z", true)]
    public void Range_ValidatesElapsedTimeRatherThanCalendarDates(string start, string end, bool valid)
    {
        var action = () => WeatherHistoryRange.Create(DateTimeOffset.Parse(start), DateTimeOffset.Parse(end), new Clock());
        if (valid) action.Should().NotThrow();
        else action.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Range_RejectsOverflowForSingleDateDefaults()
    {
        var start = () => WeatherHistoryRange.Create(DateTimeOffset.MaxValue, null, new Clock());
        var end = () => WeatherHistoryRange.Create(null, DateTimeOffset.MinValue, new Clock());
        start.Should().Throw<ArgumentException>();
        end.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public async Task Raw_PagesWithoutSkippingEqualTimestampsAcrossStations_AndPreservesWindowAndUtc()
    {
        var start = Now.AddHours(-1);
        await using var store = await Store.CreateAsync(
            Row(start.AddTicks(-1), 999), Row(start, null), Row(Now.AddMinutes(-1), 80),
            Row(Now.AddMinutes(-1), 90, "other"), Row(Now, 1000));
        var controller = store.Controller();
        var first = await controller.GetRecentRaw(null, 1, start: start, end: Now);
        var firstRows = Data<WeatherRawResponse>(first);
        firstRows.Should().ContainSingle().Which.TemperatureF.Should().Be(90);
        var before = DateTimeOffset.Parse(controller.Response.Headers["X-Weather-Next-Before"].ToString());
        var beforeId = long.Parse(controller.Response.Headers["X-Weather-Next-Before-Id"].ToString());
        var nextController = store.Controller();
        var next = await nextController.GetRecentRaw(null, 2, start: start, end: Now, before: before, beforeId: beforeId);
        var nextRows = Data<WeatherRawResponse>(next);
        nextRows.Select(row => row.TemperatureF).Should().Equal(80, null);
        nextRows.Select(row => row.Id).Should().NotContain(firstRows[0].Id);
        nextRows.Should().OnlyContain(row => row.RecordedAt.Kind == DateTimeKind.Utc);
        nextController.Response.Headers.Should().NotContainKey("X-Weather-Next-Before");
        store.Db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [TestMethod]
    public async Task Raw_DefaultWindowExcludesOlderData_AndPagingCannotEscapeWindow()
    {
        await using var store = await Store.CreateAsync(Row(Now.AddDays(-1).AddTicks(-1), 999), Row(Now.AddMinutes(-1), 70));
        var controller = store.Controller();
        Data<WeatherRawResponse>(await controller.GetRecentRaw("davis", 500)).Should().ContainSingle().Which.TemperatureF.Should().Be(70);
        controller.Response.Headers["X-Weather-Start"].ToString().Should().Be(Now.AddDays(-1).ToString("O"));
        (await controller.GetRecentRaw(null, 100, before: Now.AddDays(-2), beforeId: 1)).Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
        (await controller.GetRecentRaw(null, 100, before: Now.AddMinutes(-1))).Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
        (await controller.GetRecentRaw(null, 100, beforeId: 1)).Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
    }

    [TestMethod]
    public async Task Hourly_AggregatesOnlyRequestedPartialHoursAndStation_AndKeepsUnknownRainNull()
    {
        var start = Now.AddHours(-2).AddMinutes(30);
        var first = Row(start, 70); first.WindDirectionDegrees = 359; first.RainfallInches = .01;
        var second = Row(start.AddMinutes(5), 90); second.WindDirectionDegrees = 1;
        var third = Row(start.AddMinutes(10), null); third.WindDirectionDegrees = 1;
        await using var store = await Store.CreateAsync(first, second, third,
            Row(start.AddMinutes(-1), -999), Row(start, 999, "other"), Row(start.AddHours(1), null), Row(start.AddHours(1).AddMinutes(1), 999));
        var action = await store.Controller().GetRecentHourly("davis", 168, start: start, end: start.AddHours(1).AddMinutes(1));
        var hours = Data<WeatherHourlyResponse>(action);
        hours.Should().HaveCount(2);
        hours[0].AvgTemperatureF.Should().BeNull();
        hours[1].AvgTemperatureF.Should().Be(80);
        hours[1].MinTemperatureF.Should().Be(70);
        hours[1].MaxTemperatureF.Should().Be(90);
        hours[1].DominantWindDirectionDegrees.Should().Be(1);
        hours.Should().OnlyContain(hour => hour.TotalRainfallInches == null && hour.PeriodStart.Kind == DateTimeKind.Utc && hour.StationId == "davis");
        store.Db.WeatherHourly.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HighsLows_UsesCurrentTablesWithNullsTiesOffsetsAndStationIsolation()
    {
        var start = Now.AddHours(-1);
        var low = Row(start, 60); low.BarometricPressureInHg = 29.1; low.UvIndex = 1.5;
        var high = Row(start.AddMinutes(1), 90); high.BarometricPressureInHg = 29.9; high.UvIndex = 3.5;
        await using var store = await Store.CreateAsync(Row(start.AddTicks(-1), -999), low, high,
            Row(start.AddMinutes(2), 90), Row(start.AddMinutes(3), null), Row(start.AddMinutes(3), 999, "other"), Row(Now, 1000));
        var result = await store.Service.GetWeatherHighsLowsAsync(start.ToOffset(TimeSpan.FromHours(-7)), Now, "davis");
        var response = result.Match(success: data => data, failure: error => throw error ?? new InvalidOperationException("Missing failure"));
        response.DateRange.Start.Should().Be(start);
        response.Data.OutsideTemperatureHigh.Should().Be(90);
        response.Data.OutsideTemperatureHighDateTime.Should().Be(start.AddMinutes(1));
        response.Data.OutsideTemperatureLow.Should().Be(60);
        response.Data.OutsideTemperatureLowDateTime.Should().Be(start);
        response.Data.BarometerHigh.Should().Be(29.9m);
        response.Data.UVIndexHigh.Should().Be(3.5m);
        response.Data.InsideTemperatureHigh.Should().BeNull();
        response.Data.InsideTemperatureHighDateTime.Should().BeNull();
        response.Data.OutsideHumidityHighDateTime.Should().BeNull();
        store.Db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [TestMethod]
    public async Task HighsLows_AllMissingIsSuccessfulWithUnknownValues_EmptyRangeIsNotFound_AndCancellationPropagates()
    {
        await using var store = await Store.CreateAsync(Row(Now.AddMinutes(-1), null));
        var response = (await store.Service.GetWeatherHighsLowsAsync(null, null))
            .Match(success: data => data, failure: error => throw error ?? new InvalidOperationException("Missing failure"));
        response.Data.OutsideTemperatureHigh.Should().BeNull();
        response.Data.OutsideTemperatureHighDateTime.Should().BeNull();
        (await store.Service.GetWeatherHighsLowsAsync(Now.AddDays(-2), Now.AddDays(-1)))
            .Match(success: _ => (Exception?)null, failure: error => error).Should().BeAssignableTo<InvalidOperationException>();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Func<Task> action = () => store.Service.GetWeatherHighsLowsAsync(null, null, cancellationToken: cancel.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IReadOnlyList<T> Data<T>(ActionResult<IReadOnlyList<T>> action) =>
        (IReadOnlyList<T>)action.Result.Should().BeOfType<OkObjectResult>().Which.Value!;
    private static WeatherRaw Row(DateTimeOffset at, double? value, string station = "davis") =>
        new() { StationId = station, RecordedAt = at.UtcDateTime, TemperatureF = value };

    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public HvoV9DbContext Db { get; }
        private readonly HvoDbContext legacy;
        public WeatherService Service { get; }
        private Store(SqliteConnection connection, HvoV9DbContext db, HvoDbContext legacy)
        {
            this.connection = connection; Db = db; this.legacy = legacy;
            Service = new(legacy, db, new Clock(), NullLogger<WeatherService>.Instance);
        }
        public WeatherIngestController Controller() => new(Db, NullLogger<WeatherIngestController>.Instance, new Clock())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        public static async Task<Store> CreateAsync(params WeatherRaw[] rows)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new HvoV9DbContext(new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.WeatherRaw.AddRange(rows); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var legacy = new HvoDbContext(new DbContextOptionsBuilder<HvoDbContext>().UseSqlite(connection).Options);
            return new(connection, db, legacy);
        }
        public async ValueTask DisposeAsync()
        { await Db.DisposeAsync(); await legacy.DisposeAsync(); await connection.DisposeAsync(); }
    }
}

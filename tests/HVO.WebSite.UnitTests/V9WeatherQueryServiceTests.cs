using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class V9WeatherQueryServiceTests
{
    private SqliteConnection connection = null!;
    private HvoV9DbContext db = null!;
    private readonly ManualClock clock = new();
    private V9WeatherQueryService service = null!;
    private static readonly DateTime At = new(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc);

    [TestInitialize]
    public async Task Initialize()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        clock.Now = new DateTimeOffset(At);
        service = new(db, clock);
    }

    [TestCleanup]
    public async Task Cleanup() { await db.DisposeAsync(); await connection.DisposeAsync(); }

    [TestMethod]
    public async Task Current_ReportsNoDataThenAgesAtInclusiveThresholdAndExcludesFutureOrOtherStations()
    {
        (await service.GetLatestAsync("davis")).Should().BeNull();
        var empty = await service.GetCurrentAsync("davis");
        empty.Availability.Should().Be(V9WeatherAvailability.NoData);
        empty.Observation.Should().BeNull();
        empty.Age.Should().BeNull();
        db.WeatherRaw.AddRange(Raw("other", At, 99), Raw("davis", At.AddMinutes(1), 90), Raw("davis", At, 72));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var current = await service.GetCurrentAsync("davis");
        current.Availability.Should().Be(V9WeatherAvailability.Current);
        current.Observation!.TemperatureF.Should().Be(72);
        current.Observation.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        current.CheckedAtUtc.Should().Be(At);
        current.Age.Should().Be(TimeSpan.Zero);
        db.ChangeTracker.Entries().Should().BeEmpty();
        // Remove the future record so it cannot supersede the observation as the clock advances.
        await db.WeatherRaw.Where(row => row.RecordedAt > At).ExecuteDeleteAsync();
        clock.Now = new DateTimeOffset(At.AddMinutes(5).AddTicks(-1));
        (await service.GetCurrentAsync("davis")).Availability.Should().Be(V9WeatherAvailability.Current);
        clock.Now = new DateTimeOffset(At.AddMinutes(5));
        (await service.GetCurrentAsync("davis")).Availability.Should().Be(V9WeatherAvailability.Stale);
        (await service.GetCurrentAsync("davis", TimeSpan.FromMinutes(6))).Availability.Should().Be(V9WeatherAvailability.Current);
    }

    [TestMethod]
    public async Task RawHistory_UsesHalfOpenUtcRangesExplicitContinuationAndPreservesMissingMeasurements()
    {
        clock.Now = new DateTimeOffset(At.AddMinutes(2));
        db.WeatherRaw.AddRange(Raw("davis", At.AddMinutes(3), 88), Raw("davis", At.AddMinutes(2), null),
            Raw("davis", At, 70), Raw("davis", At.AddMinutes(1), 71), Raw("other", At, 99));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var start = new DateTimeOffset(At).ToOffset(TimeSpan.FromHours(-7));
        var end = start.AddMinutes(4);
        var page = await service.GetRawHistoryAsync("davis", start, end, 2);
        page.StartUtc.Should().Be(At);
        page.Records.Select(row => row.RecordedAtUtc).Should().Equal(At, At.AddMinutes(1));
        page.HasMore.Should().BeTrue();
        var next = await service.GetRawHistoryAsync("davis", start, end, 2, page.NextAfterUtc);
        next.Records.Should().ContainSingle().Which.TemperatureF.Should().BeNull();
        next.Records[0].RecordedAtUtc.Should().Be(At.AddMinutes(2));
        next.HasMore.Should().BeFalse();
        (await service.GetRawHistoryAsync("davis", start, start.AddMinutes(2)))
            .Records.Should().HaveCount(2);
        (await service.GetRawHistoryAsync("missing", start, end)).Records.Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [TestMethod]
    public async Task ArchiveHistory_KeepsDistinctUtcObservationsAcrossRepeatedDisplayHour()
    {
        clock.Now = new DateTimeOffset(At.AddHours(2));
        foreach (var utc in new[] { At, At.AddHours(1) })
            db.WeatherArchive.Add(new WeatherArchive
            {
                StationId = "davis", RecordedAtUtc = utc,
                ConsoleRecordedAtLocal = DateTime.SpecifyKind(At.Date.AddHours(1.5), DateTimeKind.Unspecified),
                ArchiveIntervalMinutes = 5, HighTemperatureF = 80, LowTemperatureF = 70
            });
        await db.SaveChangesAsync();
        var page = await service.GetArchiveHistoryAsync("davis", new DateTimeOffset(At), new DateTimeOffset(At.AddHours(2)), 1);
        page.HasMore.Should().BeTrue();
        var next = await service.GetArchiveHistoryAsync("davis", new DateTimeOffset(At), new DateTimeOffset(At.AddHours(2)), 1, page.NextAfterUtc);
        next.HasMore.Should().BeFalse();
        var rows = page.Records.Concat(next.Records).ToArray();
        rows.Select(row => row.RecordedAtUtc).Should().Equal(At, At.AddHours(1));
        rows.Should().OnlyContain(row => row.RecordedAtUtc.Kind == DateTimeKind.Utc && row.ConsoleRecordedAtLocal.Kind == DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var display = rows.Select(row => TimeZoneInfo.ConvertTime(new DateTimeOffset(row.RecordedAtUtc), zone)).ToArray();
        display[0].DateTime.Should().Be(display[1].DateTime);
        display[0].Offset.Should().NotBe(display[1].Offset);
        rows[0].HighTemperatureF.Should().Be(80);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)]
    public async Task InvalidHistory_IsRejectedBeforeQuery(int scenario)
    {
        var start = new DateTimeOffset(At);
        var end = start.AddDays(1);
        var limit = 100;
        DateTimeOffset? after = null;
        var station = "davis";
        switch (scenario)
        {
            case 0: station = " davis "; break;
            case 1: end = start; break;
            case 2: end = start.AddDays(31).AddTicks(1); break;
            case 3: limit = 0; break;
            case 4: limit = 1001; break;
            case 5: after = start.AddTicks(-1); break;
            case 6: after = end; break;
        }
        await FluentActions.Invoking(() => service.GetRawHistoryAsync(station, start, end, limit, after))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => service.GetArchiveHistoryAsync(station, start, end, limit, after))
            .Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task ValidationAndCancellation_PropagateWithoutInventingNoData()
    {
        foreach (var station in new[] { "", " ", new string('x', 65) })
            await FluentActions.Invoking(() => service.GetLatestAsync(station)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => service.GetCurrentAsync("davis", TimeSpan.Zero)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => service.GetCurrentAsync("davis", TimeSpan.FromDays(32))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Invoking(() => service.GetLatestAsync("davis", cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => service.GetCurrentAsync("davis", cancellationToken: cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => service.GetRawHistoryAsync("davis", new(At), new(At.AddDays(1)), cancellationToken: cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => service.GetArchiveHistoryAsync("davis", new(At), new(At.AddDays(1)), cancellationToken: cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private static WeatherRaw Raw(string station, DateTime at, double? temperature) =>
        new() { StationId = station, RecordedAt = at, TemperatureF = temperature };
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

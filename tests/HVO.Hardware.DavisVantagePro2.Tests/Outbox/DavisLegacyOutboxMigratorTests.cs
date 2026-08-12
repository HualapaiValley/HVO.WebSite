using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.Weather;
using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public sealed class DavisLegacyOutboxMigratorTests
{
    [TestMethod]
    public async Task Migration_CanonicalizesLiveAndArchivePayloadsBeforeSharedInitialization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DefaultEdgeOutboxDbContext>().UseSqlite(connection).Options;
        await using var db = new DefaultEdgeOutboxDbContext(options);
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, EdgePayloadTypes.Legacy.WeatherRaw, "1");
        db.OutboxRecords.AddRange(
            Row(EdgePayloadTypes.Legacy.WeatherRaw, new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc)),
            Row(EdgePayloadTypes.Legacy.WeatherArchive, new DateTime(2026, 8, 11, 11, 55, 0, DateTimeKind.Utc), LegacyArchiveJson()));
        await db.SaveChangesAsync();

        await DavisLegacyOutboxMigrator.MigrateAsync(db, "station-1", TimeSpan.FromHours(-7));
        await EdgeOutboxSqliteDatabaseInitializer.EnsureCreatedAsync(db, DavisOutboxPayloadTypes.Raw, "1");
        db.ChangeTracker.Clear();

        (await db.OutboxRecords.Select(row => row.PayloadType).ToListAsync())
            .Should().BeEquivalentTo([EdgePayloadTypes.WeatherRaw, EdgePayloadTypes.WeatherArchive]);
        var archive = await db.OutboxRecords.SingleAsync(row => row.PayloadType == EdgePayloadTypes.WeatherArchive);
        archive.PayloadJson.Should().Contain("\"recordedAtUtc\":\"2026-08-11T11:55:00Z\"");
        archive.PayloadJson.Should().Contain("\"consoleRecordedAtLocal\":\"2026-08-11T04:55:00\"");
        var payload = JsonSerializer.Deserialize<DavisWeatherArchivePayload>(archive.PayloadJson, JsonSerializerOptions.Web);
        payload.Should().NotBeNull();
        payload!.TemperatureF.Should().Be(70.5);
        payload.WindDirectionDegrees.Should().Be(202.5);
        payload.DownloadRecordType.Should().Be(0);
        payload.LeafWetnessScaled.Should().Equal(1, null);
        payload.SoilTemperaturesF.Should().Equal(60, 61, null, 63);
        payload.ExtraHumiditiesPercent.Should().Equal(40, null);
        payload.ExtraTemperaturesF.Should().Equal(64, null, 66);
        payload.SoilMoisturesCb.Should().Equal(10, 20, null, 40);
    }

    private static EdgeOutboxRecord Row(string type, DateTime at, string payload = "{}") => new()
    {
        SourceId = "station-1",
        PayloadType = type,
        PayloadVersion = "1",
        RecordedAtUtc = at,
        PayloadJson = payload,
    };

    private static string LegacyArchiveJson() => """
        {
          "stationId":"station-1",
          "recordedAt":"2026-08-11T11:55:00Z",
          "archiveIntervalMinutes":5,
          "temperatureF":70.5,
          "highTemperatureF":72.0,
          "lowTemperatureF":68.0,
          "windDirectionDegrees":202.5,
          "leafWetnessScaled":[1,null],
          "soilTemperaturesF":[60,61,null,63],
          "extraHumiditiesPercent":[40,null],
          "extraTemperaturesF":[64,null,66],
          "soilMoisturesCb":[10,20,null,40]
        }
        """;
}

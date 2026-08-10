using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerInventoryConfigurationProviderTests
{
    [TestMethod]
    public async Task DetailAndHistoryQueries_ReturnCompleteEg4Telemetry()
    {
        var now = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc);
        var options = new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"power-detail-provider-{Guid.NewGuid()}")
            .Options;
        await using var db = new HvoV9DbContext(options);
        var inverter = new PowerInverterDetailPayload
        {
            SourceId = "eg4-6500ex-a", SourceSystem = "eg4-6500ex", DeviceId = "inverter-a", RecordedAtUtc = now,
            Ac = new PowerInverterAcDetail { OutputVoltageV = 120.1, OutputFrequencyHz = 59.9 },
            Operating = new PowerInverterOperatingDetail { Mode = "B", LoadPercentage = 19 },
            Temperatures = [new PowerInverterTemperatureDetail { TemperatureId = "inverter", Name = "Inverter", TemperatureC = 60 }],
        };
        var mppt = new PowerMpptDetailPayload
        {
            SourceId = "eg4-mppt100-48hv-a", SourceSystem = "eg4-mppt100-48hv", DeviceId = "controller-a", RecordedAtUtc = now,
            Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "External MPPT", PowerW = 1056 }],
            BatteryOutput = new PowerMpptBatteryOutputDetail { PowerW = -494.1 },
        };
        db.PowerInverterDetailSnapshots.Add(new PowerInverterDetailSnapshot
        {
            SourceId = inverter.SourceId, SourceSystem = inverter.SourceSystem, DeviceId = inverter.DeviceId,
            RecordedAt = now, PayloadJson = JsonSerializer.Serialize(inverter, JsonOptions), CreatedAt = now,
        });
        db.PowerMpptDetailSnapshots.Add(new PowerMpptDetailSnapshot
        {
            SourceId = mppt.SourceId, SourceSystem = mppt.SourceSystem, DeviceId = mppt.DeviceId,
            RecordedAt = now, PayloadJson = JsonSerializer.Serialize(mppt, JsonOptions), CreatedAt = now, TrackerCount = 1,
        });
        db.PowerReadings.Add(new PowerReading
        {
            SourceId = inverter.SourceId, SourceSystem = inverter.SourceSystem, DeviceId = inverter.DeviceId,
            RecordedAt = now, BatteryPowerW = -1398.8,
        });
        await db.SaveChangesAsync();
        var provider = new PowerInventoryConfigurationProvider(db);

        var latestInverter = await provider.GetLatestInverterDetailAsync(inverter.SourceId, int.MaxValue);
        var latestController = await provider.GetLatestMpptDetailAsync(mppt.SourceId, int.MaxValue);
        var history = await provider.GetRecentTelemetryAsync([mppt.SourceId], [inverter.SourceId], now.AddHours(-1));

        latestInverter.Ac!.OutputVoltageV.Should().Be(120.1);
        latestInverter.Operating!.Mode.Should().Be("B");
        latestInverter.Temperatures.Should().ContainSingle();
        latestController.Trackers.Should().ContainSingle().Which.PowerW.Should().Be(1056);
        latestController.BatteryOutput!.PowerW.Should().Be(-494.1);
        history.MpptDetails.Should().ContainSingle();
        history.BatteryReadings.Should().ContainSingle().Which.PowerW.Should().Be(-1398.8);
    }

    [TestMethod]
    public async Task RecentTelemetry_WhenLimited_KeepsNewestRowsPerSourceAndBucketsChronologically()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options;
        await using var db = new HvoV9DbContext(options);
        await db.Database.EnsureCreatedAsync();
        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var payload = JsonSerializer.Serialize(new PowerMpptDetailPayload
        {
            SourceId = "solarassistant-total", DeviceId = "inverter", RecordedAtUtc = start,
            Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", PowerW = 1 }],
        }, JsonOptions);
        db.PowerMpptDetailSnapshots.AddRange(Enumerable.Range(0, 5_001).Select(index => new PowerMpptDetailSnapshot
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "inverter",
            RecordedAt = start.AddMinutes(index),
            PayloadJson = payload,
            CreatedAt = start.AddMinutes(index),
            TrackerCount = 1,
        }));
        db.PowerMpptDetailSnapshots.Add(new PowerMpptDetailSnapshot
        {
            SourceId = "eg4-mppt100-48hv-a",
            SourceSystem = "eg4-mppt100-48hv",
            DeviceId = "controller",
            RecordedAt = start,
            PayloadJson = payload,
            CreatedAt = start,
            TrackerCount = 1,
        });
        await db.SaveChangesAsync();
        var provider = new PowerInventoryConfigurationProvider(db);

        var history = await provider.GetRecentTelemetryAsync(["solarassistant-total", "eg4-mppt100-48hv-a"], [], start);

        history.MpptDetails.Should().HaveCount(1_002);
        history.MpptDetails.Count(row => row.SourceId == "solarassistant-total").Should().Be(1_001);
        history.MpptDetails.Should().ContainSingle(row => row.SourceId == "eg4-mppt100-48hv-a");
        history.MpptDetails.Where(row => row.SourceId == "solarassistant-total").First().RecordedAtUtc.Should().Be(start.AddMinutes(4));
        history.MpptDetails.Last().RecordedAtUtc.Should().Be(start.AddMinutes(5_000));
    }

    [TestMethod]
    public async Task DetailAndHistoryQueries_SkipMalformedAndFutureRows()
    {
        var now = new DateTimeOffset(2026, 8, 10, 18, 0, 0, TimeSpan.Zero);
        var options = new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"power-detail-validation-{Guid.NewGuid()}")
            .Options;
        await using var db = new HvoV9DbContext(options);
        var validPayload = JsonSerializer.Serialize(new PowerMpptDetailPayload
        {
            SourceId = "controller",
            DeviceId = "controller",
            RecordedAtUtc = now.UtcDateTime.AddMinutes(-1),
            Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", PowerW = 500 }],
        }, JsonOptions);
        db.PowerMpptDetailSnapshots.AddRange(
            MpptRow(now.UtcDateTime.AddMinutes(-1), validPayload),
            MpptRow(now.UtcDateTime.AddSeconds(-30), "{"),
            MpptRow(now.UtcDateTime, "{"),
            MpptRow(now.UtcDateTime.AddMinutes(1), validPayload));
        var validInverterPayload = JsonSerializer.Serialize(new PowerInverterDetailPayload
        {
            SourceId = "inverter",
            DeviceId = "inverter",
            RecordedAtUtc = now.UtcDateTime.AddMinutes(-1),
            Load = new PowerInverterLoadDetail { LoadPowerW = 750 },
        }, JsonOptions);
        db.PowerInverterDetailSnapshots.AddRange(
            InverterRow(now.UtcDateTime.AddMinutes(-1), validInverterPayload),
            InverterRow(now.UtcDateTime, "null"),
            InverterRow(now.UtcDateTime.AddMinutes(1), validInverterPayload));
        db.PowerReadings.AddRange(
            new PowerReading { SourceId = "inverter", DeviceId = "inverter", RecordedAt = now.UtcDateTime.AddMinutes(-1), BatteryPowerW = -400 },
            new PowerReading { SourceId = "inverter", DeviceId = "inverter", RecordedAt = now.UtcDateTime.AddMinutes(1), BatteryPowerW = -800 });
        await db.SaveChangesAsync();
        var provider = new PowerInventoryConfigurationProvider(
            db,
            Options.Create(new PowerCompositionOptions { MaxFutureClockSkewSeconds = 30 }),
            new FixedTimeProvider(now),
            NullLogger<PowerInventoryConfigurationProvider>.Instance);

        var latest = await provider.GetLatestMpptDetailAsync("controller", int.MaxValue);
        var latestInverter = await provider.GetLatestInverterDetailAsync("inverter", int.MaxValue);
        var central = await provider.GetLatestCentralAsync("inverter", int.MaxValue);
        var history = await provider.GetRecentTelemetryAsync(["controller"], ["inverter"], now.UtcDateTime.AddHours(-1));

        latest.IsPresent.Should().BeTrue();
        latest.RecordedAtUtc.Should().Be(now.UtcDateTime.AddMinutes(-1));
        latestInverter.IsPresent.Should().BeTrue();
        latestInverter.RecordedAtUtc.Should().Be(now.UtcDateTime.AddMinutes(-1));
        latestInverter.Load!.LoadPowerW.Should().Be(750);
        central.InverterDetail.IsPresent.Should().BeTrue();
        central.InverterDetail.RecordedAtUtc.Should().Be(now.UtcDateTime.AddMinutes(-1));
        history.MpptDetails.Should().ContainSingle().Which.RecordedAtUtc.Should().Be(now.UtcDateTime.AddMinutes(-1));
        history.BatteryReadings.Should().ContainSingle().Which.RecordedAtUtc.Should().Be(now.UtcDateTime.AddMinutes(-1));
    }

    private static PowerMpptDetailSnapshot MpptRow(DateTime recordedAt, string payload) => new()
    {
        SourceId = "controller",
        SourceSystem = "eg4-mppt100-48hv",
        DeviceId = "controller",
        RecordedAt = recordedAt,
        PayloadJson = payload,
        CreatedAt = recordedAt,
        TrackerCount = 1,
    };

    private static PowerInverterDetailSnapshot InverterRow(DateTime recordedAt, string payload) => new()
    {
        SourceId = "inverter",
        SourceSystem = "eg4-6500ex",
        DeviceId = "inverter",
        RecordedAt = recordedAt,
        PayloadJson = payload,
        CreatedAt = recordedAt,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

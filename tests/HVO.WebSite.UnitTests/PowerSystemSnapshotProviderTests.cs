using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerSystemSnapshotProviderTests
{
    [TestMethod]
    public async Task GetLatestAsync_DeserializesLatestMpptDetailPayload()
    {
        var now = new DateTimeOffset(2026, 5, 27, 18, 45, 0, TimeSpan.Zero);
        var dbOptions = new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"power-mppt-provider-{Guid.NewGuid()}")
            .Options;
        await using var db = new HvoV9DbContext(dbOptions);
        var payload = new PowerMpptDetailPayload
        {
            SourceId = "eg4-6500ex-a",
            SourceSystem = "eg4-6500ex",
            DeviceId = "inverter-a",
            RecordedAtUtc = now.UtcDateTime,
            Trackers =
            [
                new PowerMpptTrackerDetail
                {
                    TrackerId = "mppt-1",
                    Name = "PV 1",
                    PowerW = 725,
                    Provenance = PowerObservationProvenance.Direct,
                    Confidence = "independent",
                },
            ],
        };
        db.PowerMpptDetailSnapshots.AddRange(
            new PowerMpptDetailSnapshot
            {
                SourceId = payload.SourceId,
                SourceSystem = payload.SourceSystem,
                DeviceId = payload.DeviceId,
                RecordedAt = payload.RecordedAtUtc.AddMinutes(-1),
                PayloadJson = JsonSerializer.Serialize(new PowerMpptDetailPayload
                {
                    SourceId = payload.SourceId,
                    SourceSystem = payload.SourceSystem,
                    DeviceId = payload.DeviceId,
                    RecordedAtUtc = payload.RecordedAtUtc.AddMinutes(-1),
                    Trackers = [new PowerMpptTrackerDetail { TrackerId = "mppt-1", Name = "PV 1", PowerW = 100 }],
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                TrackerCount = 1,
                CreatedAt = payload.RecordedAtUtc.AddMinutes(-1),
            },
            new PowerMpptDetailSnapshot
            {
                SourceId = payload.SourceId,
                SourceSystem = payload.SourceSystem,
                DeviceId = payload.DeviceId,
                RecordedAt = payload.RecordedAtUtc,
                PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                TrackerCount = 1,
                CreatedAt = payload.RecordedAtUtc,
            });
        var inverterPayload = new PowerInverterDetailPayload
        {
            SourceId = "eg4-6500ex-a",
            SourceSystem = "eg4-6500ex",
            DeviceId = "inverter-a",
            RecordedAtUtc = now.UtcDateTime.AddMinutes(-1),
            Ac = new PowerInverterAcDetail { OutputVoltageV = 120.1, OutputFrequencyHz = 59.9 },
            Load = new PowerInverterLoadDetail { LoadPowerW = 875 },
            Operating = new PowerInverterOperatingDetail { Mode = "Battery", LoadPercentage = 19 },
        };
        db.PowerInverterDetailSnapshots.AddRange(
            new PowerInverterDetailSnapshot
            {
                SourceId = inverterPayload.SourceId,
                SourceSystem = inverterPayload.SourceSystem,
                DeviceId = inverterPayload.DeviceId,
                RecordedAt = inverterPayload.RecordedAtUtc,
                PayloadJson = JsonSerializer.Serialize(inverterPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CreatedAt = inverterPayload.RecordedAtUtc,
            },
            new PowerInverterDetailSnapshot
            {
                SourceId = inverterPayload.SourceId,
                SourceSystem = inverterPayload.SourceSystem,
                DeviceId = inverterPayload.DeviceId,
                RecordedAt = now.UtcDateTime,
                PayloadJson = "null",
                CreatedAt = now.UtcDateTime,
            });
        await db.SaveChangesAsync();
        var compositionOptions = new PowerCompositionOptions
        {
            ExpectedPvTrackerIds = ["eg4-6500ex-a/mppt-1"],
        };
        var provider = new PowerSystemSnapshotProvider(
            db, Options.Create(compositionOptions), new FixedTimeProvider(now));

        var snapshot = await provider.GetLatestAsync(10);

        snapshot!.Pv!.Trackers.Should().ContainSingle();
        snapshot.Pv.Trackers![0].TrackerId.Should().Be("eg4-6500ex-a/mppt-1");
        snapshot.Pv.Trackers[0].PowerW.Should().Be(725);
        snapshot.Pv.PowerW!.Source.Should().Be(PowerMetricSource.Derived);
        snapshot.Ac!.LoadPowerW!.Value.Should().Be(875);
        snapshot.Ac.LoadPowerW.Source.Should().Be(PowerMetricSource.Eg46500Ex);
        snapshot.Ac.OutputVoltageV!.Value.Should().Be(120.1);
        snapshot.Ac.InverterMode!.Value.Should().Be("Battery");
    }

    [TestMethod]
    public async Task GetLatestAsync_SkipsInvalidPersistedMpptPayloads()
    {
        var now = new DateTimeOffset(2026, 5, 27, 18, 45, 0, TimeSpan.Zero);
        var dbOptions = new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase($"power-invalid-mppt-provider-{Guid.NewGuid()}")
            .Options;
        await using var db = new HvoV9DbContext(dbOptions);
        db.PowerReadings.Add(new PowerReading
        {
            SourceId = "solarassistant-total",
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAt = now.UtcDateTime,
            PvPowerW = 1200,
        });
        db.PowerMpptDetailSnapshots.AddRange(
            InvalidSnapshot("invalid-json", now.UtcDateTime, "{"),
            InvalidSnapshot("null-trackers", now.UtcDateTime, """{"sourceId":"null-trackers","trackers":null}"""));
        await db.SaveChangesAsync();
        var provider = new PowerSystemSnapshotProvider(
            db, Options.Create(new PowerCompositionOptions()), new FixedTimeProvider(now));

        var snapshot = await provider.GetLatestAsync(10);

        snapshot!.Pv.Should().BeNull();
        snapshot.Ac.Should().BeNull();
        snapshot.Battery.Should().BeNull();
        snapshot.BatteryObservations.Should().BeNull();
    }

    private static PowerMpptDetailSnapshot InvalidSnapshot(string sourceId, DateTime recordedAt, string payloadJson) => new()
    {
        SourceId = sourceId,
        SourceSystem = "eg4-mppt100-48hv",
        DeviceId = sourceId,
        RecordedAt = recordedAt,
        PayloadJson = payloadJson,
        CreatedAt = recordedAt,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

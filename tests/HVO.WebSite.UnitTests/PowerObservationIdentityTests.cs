using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Infrastructure;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using HVO.WebSite.v9.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerObservationIdentityTests
{
    [TestMethod]
    public async Task BlankInverterReadPreservesMissingResponseInsteadOfThrowing()
    {
        await using var fixture = new Fixture();
        var latest = await fixture.LatestAsync("inverter", " ");
        latest.GetProperty("isPresent").GetBoolean().Should().BeFalse();
        latest.GetProperty("isStale").GetBoolean().Should().BeTrue();
    }

    [TestMethod]
    [DataRow("inventory"), DataRow("configuration"), DataRow("energy")]
    [DataRow("inverter"), DataRow("mppt"), DataRow("gateway")]
    public async Task RecurringAndConstantConfirmationsPreserveLatestState_AndReplayIdentity(string kind)
    {
        await using var fixture = new Fixture();
        var now = fixture.Clock.Now.UtcDateTime;
        foreach (var (at, value) in new[] { (now.AddMinutes(-5), 100), (now.AddMinutes(-1), 200), (now, 100) })
            AssertInserted(await fixture.IngestAsync(kind, "source-one", at, value));

        var latest = await fixture.LatestAsync(kind, "source-one");
        latest.GetProperty("recordedAtUtc").GetDateTime().Should().Be(now);
        Value(latest, kind).Should().Be(Value(Payload(kind, "source-one", now, 100), kind));
        latest.GetProperty("isStale").GetBoolean().Should().BeFalse();
        AssertSkipped(await fixture.IngestAsync(kind, "source-one", now, 100));
        (await fixture.CountAsync(kind)).Should().Be(3);

        // Reconfirming the same content is distinct from replaying the same observation.
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        (await fixture.LatestAsync(kind, "source-one")).GetProperty("isStale").GetBoolean().Should().BeTrue();
        var confirmed = fixture.Clock.Now.UtcDateTime;
        AssertInserted(await fixture.IngestAsync(kind, "source-one", confirmed, 100));
        (await fixture.LatestAsync(kind, "source-one")).GetProperty("isStale").GetBoolean().Should().BeFalse();
        (await fixture.LatestAsync(kind, "source-one")).GetProperty("recordedAtUtc").GetDateTime().Should().Be(confirmed);
        AssertSkipped(await fixture.IngestAsync(kind, "source-one", confirmed, 100));
        (await fixture.CountAsync(kind)).Should().Be(4);

        AssertInserted(await fixture.IngestAsync(kind, "source-two", confirmed, 200));
        Value(await fixture.LatestAsync(kind, "source-one"), kind).Should().Be(Value(Payload(kind, "source-one", confirmed, 100), kind));
        Value(await fixture.LatestAsync(kind, "source-two"), kind).Should().Be(Value(Payload(kind, "source-two", confirmed, 200), kind));
        (await fixture.CountAsync(kind)).Should().Be(5);
    }

    [TestMethod]
    [DataRow("inventory"), DataRow("configuration"), DataRow("energy")]
    [DataRow("inverter"), DataRow("mppt"), DataRow("gateway")]
    public async Task DifferentContentAtSameIdentityIsNotAcknowledged_OrAllowedToReplaceHistory(string kind)
    {
        await using var fixture = new Fixture();
        var now = fixture.Clock.Now.UtcDateTime;
        AssertInserted(await fixture.IngestAsync(kind, "source", now, 100));
        var conflict = await fixture.IngestAsync(kind, "source", now, 200);
        conflict.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
        (await fixture.CountAsync(kind)).Should().Be(1);
        Value(await fixture.LatestAsync(kind, "source"), kind).Should().Be(Value(Payload(kind, "source", now, 100), kind));
        AssertSkipped(await fixture.IngestAsync(kind, "source", now, 100));
    }

    [TestMethod]
    [DataRow("inventory"), DataRow("configuration"), DataRow("energy")]
    [DataRow("inverter"), DataRow("mppt"), DataRow("gateway")]
    public async Task LateAndFutureObservationsDoNotReplaceCurrentEvidence_AndMissingTimeIsRejected(string kind)
    {
        await using var fixture = new Fixture();
        var now = fixture.Clock.Now.UtcDateTime;
        AssertInserted(await fixture.IngestAsync(kind, "source", now, 100));
        AssertInserted(await fixture.IngestAsync(kind, "source", now.AddMinutes(-10), 200));
        AssertInserted(await fixture.IngestAsync(kind, "source", now.AddHours(1), 200));
        var latest = await fixture.LatestAsync(kind, "source");
        latest.GetProperty("recordedAtUtc").GetDateTime().Should().Be(now);
        Value(latest, kind).Should().Be(Value(Payload(kind, "source", now, 100), kind));
        (await fixture.IngestAsync(kind, "source", default, 200)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await fixture.CountAsync(kind)).Should().Be(3, "late/future rows are retained for diagnosis, but missing time is invalid");
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        (await fixture.LatestAsync(kind, "source")).GetProperty("isStale").GetBoolean().Should().BeTrue();
    }

    private static void AssertInserted(ActionResult<PowerSnapshotIngestResponse> result) =>
        result.Result.Should().BeOfType<CreatedAtActionResult>().Which.Value.Should().BeEquivalentTo(new { Inserted = true, Skipped = false });
    private static void AssertSkipped(ActionResult<PowerSnapshotIngestResponse> result) =>
        result.Result.Should().BeOfType<CreatedAtActionResult>().Which.Value.Should().BeEquivalentTo(new { Inserted = false, Skipped = true });

    private static JsonElement Payload(string kind, string source, DateTime at, int value)
    {
        object payload = kind switch
        {
            "inventory" => new PowerDeviceInventoryPayload { SourceId = source, SourceSystem = "test", RecordedAtUtc = at, Devices = [new() { DeviceId = "device", Name = $"State{value}" }] },
            "configuration" => new PowerConfigurationPayload { SourceId = source, SourceSystem = "test", RecordedAtUtc = at, Settings = [new() { Key = "mode", Name = "Mode", Value = $"State{value}" }] },
            "energy" => new PowerEnergyPayload { SourceId = source, SourceSystem = "test", RecordedAtUtc = at, Counters = [new() { Key = "total", Name = "Total", ValueKwh = value }] },
            "inverter" => new PowerInverterDetailPayload { SourceId = source, SourceSystem = "test", RecordedAtUtc = at, Load = new() { LoadPowerW = value } },
            "mppt" => new PowerMpptDetailPayload { SourceId = source, SourceSystem = "test", RecordedAtUtc = at, Trackers = [new() { TrackerId = "one", Name = "Tracker", PowerW = value, Provenance = PowerObservationProvenance.Direct }] },
            "gateway" => new GatewayStatusPayload { SourceId = source, SourceSystem = "test", RecordedAtUtc = at,
                Identity = new("gateway", $"State{value}", GatewayDomain.Power, source),
                Health = new(GatewayHealthState.Healthy, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), [], GatewaySampleState.Live),
                Rest = new(GatewaySampleState.Live), Outbox = new(0, 0) },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web);
    }

    private static string Value(JsonElement payload, string kind) => kind switch
    {
        "inventory" => payload.GetProperty("devices")[0].GetProperty("name").GetRawText(),
        "configuration" => payload.GetProperty("settings")[0].GetProperty("value").GetRawText(),
        "energy" => payload.GetProperty("counters")[0].GetProperty("valueKwh").GetRawText(),
        "inverter" => payload.GetProperty("load").GetProperty("loadPowerW").GetRawText(),
        "mppt" => payload.GetProperty("trackers")[0].GetProperty("powerW").GetRawText(),
        "gateway" => payload.GetProperty("identity").GetProperty("displayName").GetRawText(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("DataSource=:memory:");
        private readonly HvoV9DbContext db;
        private readonly PowerIngestTelemetry telemetry = new();
        private readonly PowerIngestController controller;
        public Clock Clock { get; } = new();
        public Fixture()
        {
            connection.Open();
            db = new(new DbContextOptionsBuilder<HvoV9DbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
            var options = Options.Create(new PowerCompositionOptions());
            controller = new(db, NullLogger<PowerIngestController>.Instance,
                new PowerReadingIngestService(db, telemetry, NullLogger<PowerReadingIngestService>.Instance),
                new PowerSystemSnapshotProvider(db),
                new PowerInventoryConfigurationProvider(db, options, Clock), Clock, options)
            {
                ControllerContext = new() { HttpContext = new DefaultHttpContext() },
                ProblemDetailsFactory = new DefaultProblemDetailsFactory(Options.Create(new ApiBehaviorOptions())),
            };
        }
        public Task<ActionResult<PowerSnapshotIngestResponse>> IngestAsync(string kind, string source, DateTime at, int value)
        {
            var payload = Payload(kind, source, at, value);
            return kind switch
            {
                "inventory" => controller.IngestDeviceInventory(payload.Deserialize<PowerDeviceInventoryPayload>(JsonSerializerOptions.Web)!, default),
                "configuration" => controller.IngestConfiguration(payload.Deserialize<PowerConfigurationPayload>(JsonSerializerOptions.Web)!, default),
                "energy" => controller.IngestEnergy(payload.Deserialize<PowerEnergyPayload>(JsonSerializerOptions.Web)!, default),
                "inverter" => controller.IngestInverterDetail(payload.Deserialize<PowerInverterDetailPayload>(JsonSerializerOptions.Web)!, default),
                "mppt" => controller.IngestMpptDetail(payload.Deserialize<PowerMpptDetailPayload>(JsonSerializerOptions.Web)!, default),
                "gateway" => controller.IngestGatewayStatus(payload.Deserialize<GatewayStatusPayload>(JsonSerializerOptions.Web)!, default),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        public async Task<JsonElement> LatestAsync(string kind, string source)
        {
            object? value = kind switch
            {
                "inventory" => ((OkObjectResult)(await controller.GetLatestDeviceInventory(source, 5)).Result!).Value,
                "configuration" => ((OkObjectResult)(await controller.GetLatestConfiguration(source, 5)).Result!).Value,
                "energy" => ((OkObjectResult)(await controller.GetLatestEnergy(source, 5)).Result!).Value,
                "inverter" => ((OkObjectResult)(await controller.GetLatestInverterDetail(source, 5)).Result!).Value,
                "mppt" => ((OkObjectResult)(await controller.GetLatestMpptDetail(source, 5)).Result!).Value,
                "gateway" => ((OkObjectResult)(await controller.GetLatestGatewayStatus(source, 5)).Result!).Value,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);
        }
        public Task<int> CountAsync(string kind) => kind switch
        {
            "inventory" => db.PowerDeviceInventorySnapshots.CountAsync(), "configuration" => db.PowerConfigurationSnapshots.CountAsync(),
            "energy" => db.PowerEnergySnapshots.CountAsync(), "inverter" => db.PowerInverterDetailSnapshots.CountAsync(),
            "mppt" => db.PowerMpptDetailSnapshots.CountAsync(), "gateway" => db.GatewayStatusSnapshots.CountAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        public async ValueTask DisposeAsync() { telemetry.Dispose(); await db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

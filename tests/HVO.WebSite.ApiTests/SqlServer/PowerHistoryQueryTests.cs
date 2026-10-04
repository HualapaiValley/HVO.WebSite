using System.Text.Json;
using FluentAssertions;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.Themes.Components.Format;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass, TestCategory("Integration"), TestCategory("SqlServerIntegration"), DoNotParallelize]
public sealed class PowerHistoryQueryTests
{
    private static readonly DateTime Start = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    [TestMethod]
    public async Task FortyEightHoursAtFifteenSecondCadence_PreservesBothEnds_AndMaterializesOnlySelectedDetails()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var end = Start.AddHours(48);
        await using (var seed = database.CreateContext())
        {
            var json = Payload(100);
            for (var index = 0; index <= 11_520; index++)
            {
                var at = Start.AddSeconds(index * 15);
                seed.PowerMpptDetailSnapshots.Add(Mppt("wanted", at, json));
                seed.PowerReadings.Add(new() { SourceId = "battery", DeviceId = "one", RecordedAt = at, BatteryPowerW = -100 });
            }
            seed.PowerMpptDetailSnapshots.Add(Mppt("foreign", Start, Payload(900)));
            seed.PowerMpptDetailSnapshots.Add(Mppt("wanted", Start.AddSeconds(-1), Payload(900)));
            seed.PowerMpptDetailSnapshots.Add(Mppt("wanted", end.AddSeconds(1), Payload(900)));
            seed.PowerReadings.Add(new() { SourceId = "battery", DeviceId = "two", RecordedAt = Start.AddSeconds(1), BatteryPowerW = 20 });
            seed.PowerReadings.Add(new() { SourceId = "foreign", DeviceId = "one", RecordedAt = Start, BatteryPowerW = 900 });
            await seed.SaveChangesAsync();
        }
        var materialized = new DetailMaterializationCounter();
        await using var db = database.CreateContext(materialized);
        var clock = new CountingClock(end);
        var provider = new PowerInventoryConfigurationProvider(db, Options.Create(new PowerCompositionOptions()), clock);
        var history = await provider.GetTelemetryWindowAsync(["wanted"], ["battery"], Start, end);

        history.MpptDetails.Should().HaveCount(577).And.OnlyContain(row => row.SourceId == "wanted");
        history.MpptDetails.First().RecordedAtUtc.Should().Be(Start.AddMinutes(4).AddSeconds(45));
        history.MpptDetails.Last().RecordedAtUtc.Should().Be(end);
        history.MpptDetails.Should().OnlyContain(row => row.RecordedAtUtc.Kind == DateTimeKind.Utc);
        materialized.Details.Should().Be(577, "metadata selection must precede JSON entity materialization, rather than reading more than 11,000 detail payloads");
        history.BatteryReadings.Where(row => row.DeviceId == "one").Should().HaveCount(577);
        history.BatteryReadings.Should().ContainSingle(row => row.DeviceId == "two");
        history.BatteryReadings.Should().OnlyContain(row => row.SourceId == "battery" && row.RecordedAtUtc.Kind == DateTimeKind.Utc);
        var view = new PowerDashboardHistoryPresenter(history, new() { ExpectedPvTrackerIds = ["wanted/pv"] }, Start, end, new("America/Phoenix"));
        view.Labels.Should().HaveCount(577);
        view.PvDatasets.First().Data.First().Should().Be(100);
        view.PvDatasets.First().Data.Last().Should().Be(100);
        view.BatteryDatasets.Single(dataset => dataset.Label == "battery / one").Data.Should().OnlyContain(value => value == 100);
    }

    [TestMethod]
    public async Task InvalidRepresentatives_TryBoundedOlderCandidates_ThenLeaveExplicitGapsWithoutCrossSourceFallback()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using (var seed = database.CreateContext())
        {
            seed.PowerMpptDetailSnapshots.AddRange(
                Mppt("wanted", Start.AddSeconds(10), Payload(100)),
                Mppt("wanted", Start.AddSeconds(20), "{"), Mppt("wanted", Start.AddSeconds(30), "null"),
                Mppt("wanted", Start.AddMinutes(5).AddSeconds(1), Payload(200)),
                Mppt("wanted", Start.AddMinutes(5).AddSeconds(10), "{"),
                Mppt("wanted", Start.AddMinutes(5).AddSeconds(20), "null"),
                Mppt("wanted", Start.AddMinutes(5).AddSeconds(30), "{\"trackers\":null}"),
                Mppt("foreign", Start.AddMinutes(5), Payload(900)));
            await seed.SaveChangesAsync();
        }
        var materialized = new DetailMaterializationCounter();
        await using var db = database.CreateContext(materialized);
        var provider = new PowerInventoryConfigurationProvider(db, Options.Create(new PowerCompositionOptions()),
            new CountingClock(Start.AddMinutes(10)), NullLogger<PowerInventoryConfigurationProvider>.Instance);
        var history = await provider.GetTelemetryWindowAsync(["wanted"], [], Start, Start.AddMinutes(10));
        history.MpptDetails.Should().ContainSingle().Which.RecordedAtUtc.Should().Be(Start.AddSeconds(10));
        materialized.Details.Should().Be(6, "at most three payload candidates per bucket are read, and other sources cannot fill a gap");
        var view = new PowerDashboardHistoryPresenter(history, new() { ExpectedPvTrackerIds = ["wanted/pv"] }, Start, Start.AddMinutes(10), new());
        view.PvDatasets.First().Data.Should().Equal(100, null, null);
        view.Coverage.Should().Contain("Partial coverage");
    }

    [TestMethod]
    public async Task ActualSqlUnspecifiedTimestamps_RestoreUtcKinds_AndFakeClockControlsAllFreshness()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using (var seed = database.CreateContext())
        {
            seed.PowerMpptDetailSnapshots.Add(Mppt("wanted", Start, Payload(100)));
            seed.PowerDeviceInventorySnapshots.Add(new() { SourceId = "wanted", RecordedAt = Start, CreatedAt = Start, PayloadHash = "inventory", PayloadJson = "{}" });
            seed.PowerConfigurationSnapshots.Add(new() { SourceId = "wanted", RecordedAt = Start, CreatedAt = Start, PayloadHash = "configuration", PayloadJson = "{}" });
            seed.PowerReadings.Add(new() { SourceId = "solarassistant-total", SourceSystem = "solarassistant", RecordedAt = Start, LoadPowerW = 123 });
            seed.PowerInverterDetailSnapshots.Add(new()
            {
                SourceId = "inverter", SourceSystem = "eg4-6500ex", RecordedAt = Start, CreatedAt = Start, PayloadHash = "inverter",
                PayloadJson = JsonSerializer.Serialize(new PowerInverterDetailPayload
                {
                    SourceId = "inverter", SourceSystem = "eg4-6500ex",
                    RecordedAtUtc = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified), Load = new() { LoadPowerW = 123 },
                }, JsonOptions),
            });
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext();
        var clock = new CountingClock(Start.AddMinutes(1));
        var options = Options.Create(new PowerCompositionOptions());
        var provider = new PowerInventoryConfigurationProvider(db, options, clock);
        var pair = await provider.GetLatestAsync("wanted", 5);
        clock.Calls.Should().Be(1, "the cutoff and both payload freshness mappings share one clock instant");
        pair.Inventory.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        pair.Configuration.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        pair.Inventory.IsStale.Should().BeFalse(); pair.Configuration.IsStale.Should().BeFalse();
        var detail = await provider.GetLatestMpptDetailAsync("wanted");
        detail.RecordedAtUtc.Should().Be(Start); detail.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc); detail.IsStale.Should().BeFalse();
        clock.Now = Start.AddMinutes(6);
        (await provider.GetLatestMpptDetailAsync("wanted")).IsStale.Should().BeTrue();
        (await provider.GetLatestAsync("wanted", 5)).Inventory.IsStale.Should().BeTrue();
        clock.Now = Start.AddMinutes(1);
        var snapshot = await new PowerSystemSnapshotProvider(db, options, clock).GetLatestAsync();
        snapshot!.Ac!.LoadPowerW!.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        snapshot.Ac.LoadPowerW.RecordedAtUtc.Should().Be(Start);
        var view = PowerStatusViewModel.FromSnapshot(snapshot, options.Value, clock.Now, new("America/Phoenix"));
        view.ObservedAt.Should().Contain("America/Phoenix");
        Console.WriteLine($"Actual SQL host zone: {TimeZoneInfo.Local.Id}; UTC restored and fake time controlled freshness.");
    }

    private static PowerMpptDetailSnapshot Mppt(string source, DateTime at, string json) => new()
    {
        SourceId = source, SourceSystem = "test", DeviceId = "controller", RecordedAt = at, CreatedAt = at, PayloadJson = json,
    };
    private static string Payload(double power) => JsonSerializer.Serialize(new PowerMpptDetailPayload
    {
        SourceId = "wanted", RecordedAtUtc = Start, Trackers = [new() { TrackerId = "pv", Name = "PV", PowerW = power }],
    }, JsonOptions);
    private sealed class DetailMaterializationCounter : IMaterializationInterceptor
    {
        public int Details { get; private set; }
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is PowerMpptDetailSnapshot) Details++;
            return entity;
        }
    }
    private sealed class CountingClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public int Calls { get; private set; }
        public override DateTimeOffset GetUtcNow() { Calls++; return new(Now); }
    }
}

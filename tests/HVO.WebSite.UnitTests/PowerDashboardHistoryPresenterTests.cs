using FluentAssertions;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.Themes.Components.Format;
using HVO.WebSite.v9.Configuration;
using HVO.WebSite.v9.Models;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class PowerDashboardHistoryPresenterTests
{
    private static readonly DateTime Start = new(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly PowerCompositionOptions Options = new() { ExpectedPvTrackerIds = ["a/pv", "b/pv"], MaxDerivationSkewSeconds = 30 };

    [TestMethod]
    public void CompleteUtcGrid_PreservesGlobalPartialLeadingAndTrailingOutages()
    {
        var history = new PowerTelemetryHistoryResponse(
            [Detail("a", Start.AddMinutes(5), 100), Detail("b", Start.AddMinutes(5), 200),
             Detail("a", Start.AddMinutes(10), 110), Detail("a", Start.AddMinutes(70), 120), Detail("b", Start.AddMinutes(70), 220)],
            [new(Start.AddMinutes(5), "battery", "one", -300), new(Start.AddMinutes(70), "battery", "one", 400)]);
        var view = new PowerDashboardHistoryPresenter(history, Options, Start, Start.AddMinutes(80), new());

        view.TimesUtc.Should().HaveCount(17).And.StartWith(Start).And.EndWith(Start.AddMinutes(80));
        view.TimesUtc.Zip(view.TimesUtc.Skip(1), (a, b) => b - a).Should().OnlyContain(interval => interval == TimeSpan.FromMinutes(5));
        var a = view.PvDatasets.Single(dataset => dataset.Label == "a/pv").Data;
        var b = view.PvDatasets.Single(dataset => dataset.Label == "b/pv").Data;
        var subtotal = view.PvDatasets.Single(dataset => dataset.Label == "5-minute PV subtotal").Data;
        a[0].Should().BeNull(); a[1].Should().Be(100); a[2].Should().Be(110);
        b[2].Should().BeNull(); subtotal[1].Should().Be(300); subtotal[2].Should().BeNull();
        a.Skip(3).Take(11).Should().OnlyContain(value => value == null);
        subtotal[14].Should().Be(340); a[15].Should().BeNull(); a[16].Should().BeNull();
        view.BatteryDatasets.Single().Data[1].Should().Be(300);
        view.BatteryDatasets.Single().Data[14].Should().Be(-400);
        view.PvDatasets.Concat(view.BatteryDatasets).Should().OnlyContain(dataset => dataset.Data.Count == view.Labels.Count);
        view.Coverage.Should().Contain("Partial coverage");
    }

    [TestMethod]
    public void Subtotal_RequiresEveryExpectedTrackerAndAcceptableObservationSkew()
    {
        var view = new PowerDashboardHistoryPresenter(new(
            [Detail("a", Start.AddSeconds(1), 100), Detail("b", Start.AddSeconds(40), 200),
             Detail("a", Start.AddMinutes(5).AddSeconds(1), 110), Detail("b", Start.AddMinutes(5).AddSeconds(20), 210)], []),
            Options, Start, Start.AddMinutes(10), new());
        var subtotal = view.PvDatasets.Last().Data;
        subtotal.Should().Equal(null, 320, null);
    }

    [TestMethod]
    public void SourceAndDeviceIsolation_PreservesIndependentBatteryGapsAndCanonicalSigns()
    {
        var view = new PowerDashboardHistoryPresenter(new(
            [Detail("foreign", Start, 900), Detail("a", Start, 100)],
            [new(Start, "battery", "one", -50), new(Start.AddMinutes(5), "battery", "two", 70), new(Start, "other", "one", null)]),
            Options, Start, Start.AddMinutes(10), new());
        view.PvDatasets.Single(dataset => dataset.Label == "a/pv").Data.Should().Equal(100, null, null);
        view.PvDatasets.Should().NotContain(dataset => dataset.Label.Contains("foreign"));
        view.BatteryDatasets.Single(dataset => dataset.Label == "battery / one").Data.Should().Equal(50, null, null);
        view.BatteryDatasets.Single(dataset => dataset.Label == "battery / two").Data.Should().Equal(null, -70, null);
        view.BatteryDatasets.Single(dataset => dataset.Label == "other / one").Data.Should().OnlyContain(value => value == null);
    }

    [TestMethod]
    public void Representatives_AreDeterministicOnObservationTimeThenId_AndExcludeOutsideWindow()
    {
        var selected = Detail("a", Start.AddMinutes(5), 300, 3);
        var view = new PowerDashboardHistoryPresenter(new(
            [selected, Detail("a", Start.AddMinutes(5), 100, 1), Detail("a", Start.AddMinutes(4), 200, 2),
             Detail("a", Start.AddSeconds(-1), 900), Detail("a", Start.AddMinutes(10).AddTicks(1), 900)], []),
            Options, Start, Start.AddMinutes(10), new());
        view.PvDatasets.First().Data.Should().Equal(200, 300, null);
    }

    [TestMethod]
    [DataRow(DateTimeKind.Utc)]
    [DataRow(DateTimeKind.Unspecified)]
    public void StoredUtcRepresentation_HasIdenticalGridLabelsAndAges_InTheExplicitDisplayZone(DateTimeKind kind)
    {
        var at = DateTime.SpecifyKind(Start, kind);
        var view = new PowerDashboardHistoryPresenter(new([Detail("a", at, 100)], [new(at, "battery", "one", -100)]),
            Options, at, DateTime.SpecifyKind(Start.AddMinutes(10), kind), new("America/Phoenix"));
        view.TimesUtc.Should().Equal(Start, Start.AddMinutes(5), Start.AddMinutes(10));
        view.TimesUtc.Should().OnlyContain(time => time.Kind == DateTimeKind.Utc);
        view.Labels.Should().Equal("17:00", "17:05", "17:10");
        view.DisplayTimeZoneLabel.Should().Be("America/Phoenix");
        var snapshot = new PowerSystemSnapshot(at, Ac: new(LoadPowerW: new(100, PowerMetricSource.Eg46500Ex, at)));
        var live = PowerStatusViewModel.FromSnapshot(snapshot, new() { Eg4BranchFreshnessSeconds = 10 }, Start, new("America/Phoenix"));
        var stale = PowerStatusViewModel.FromSnapshot(snapshot, new() { Eg4BranchFreshnessSeconds = 10 }, Start.AddSeconds(11), new("America/Phoenix"));
        live.SnapshotState.Should().Be("Live"); stale.SnapshotState.Should().Be("Stale");
        live.ObservedAt.Should().Contain("5:00 PM").And.Contain("America/Phoenix");
        Console.WriteLine($"Host zone: {TimeZoneInfo.Local.Id}; stored kind: {kind}; display: {view.DisplayTimeZoneLabel}");
    }

    [TestMethod]
    public void LongAndEmptyWindows_AreBoundedAndExplicit_IncludingFirstAndLastBuckets()
    {
        var empty = new PowerDashboardHistoryPresenter(PowerTelemetryHistoryResponse.Empty, Options, Start, Start.AddHours(48), new("UTC"));
        empty.TimesUtc.Should().HaveCount(577).And.StartWith(Start).And.EndWith(Start.AddHours(48));
        empty.Labels.First().Should().Be("10-04 00:00"); empty.Labels.Last().Should().Be("10-06 00:00");
        empty.PvDatasets.Should().BeEmpty(); empty.BatteryDatasets.Should().BeEmpty();
        empty.Coverage.Should().Be("No observations in this window.");
        var invalid = () => new PowerDashboardHistoryPresenter(PowerTelemetryHistoryResponse.Empty, Options, Start, Start.AddHours(49), new());
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var reversed = () => new PowerDashboardHistoryPresenter(PowerTelemetryHistoryResponse.Empty, Options, Start, Start, new());
        reversed.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void DisplayPolicy_FallsBackToVisibleUtc_AndRejectsAmbiguousLocalInputs()
    {
        var view = new PowerDashboardHistoryPresenter(new([Detail("a", Start, 100)], []), Options, Start, Start.AddMinutes(5), new("invalid-zone"));
        view.DisplayTimeZoneLabel.Should().Be("UTC"); view.Labels.First().Should().Be("00:00");
        var local = () => new PowerDashboardHistoryPresenter(PowerTelemetryHistoryResponse.Empty, Options, DateTime.SpecifyKind(Start, DateTimeKind.Local), Start.AddMinutes(5), new());
        local.Should().Throw<ArgumentException>();
    }

    private static PowerMpptDetailSnapshotResponse Detail(string source, DateTime at, double power, long id = 1) => new()
    {
        Id = id, SourceId = source, RecordedAtUtc = at, IsPresent = true,
        Trackers = [new() { TrackerId = "pv", Name = "PV", PowerW = power }],
    };
}

using Bunit;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Components.Pages;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using MudBlazor.Services;

namespace HVO.Gateway.SolarAssistant.Tests.Components;

[TestClass]
public sealed class PowerHistoryChartBunitTests : BunitContext
{
    public PowerHistoryChartBunitTests() => Services.AddMudServices();

    [TestMethod]
    public void RendersEmptyStateWithoutData()
    {
        var component = Render<PowerHistoryChart>(parameters => parameters
            .Add(p => p.Title, "PV Power")
            .Add(p => p.Points, Array.Empty<PowerSnapshotHistoryPoint>())
            .Add(p => p.ValueSelector, point => point.PvPowerW));

        component.Markup.Should().Contain("Waiting for history data");
        component.Markup.Should().Contain("No samples");
        component.Markup.Should().Contain("--");
    }

    [TestMethod]
    public void RendersHvoChartWhenDataPresent()
    {
        var component = Render<PowerHistoryChart>(parameters => parameters
            .Add(p => p.Title, "PV Power")
            .Add(p => p.Points, Points())
            .Add(p => p.ValueSelector, point => point.PvPowerW));

        component.Find("canvas").Id.Should().StartWith("phc-");
        component.Markup.Should().Contain("2 samples");
        component.Markup.Should().Contain("300 W");
    }

    [TestMethod]
    public void FormatsRangeSummary()
    {
        var component = Render<PowerHistoryChart>(parameters => parameters
            .Add(p => p.Title, "Load Power")
            .Add(p => p.Points, Points())
            .Add(p => p.ValueSelector, point => point.LoadPowerW));

        component.Markup.Should().Contain("120 W to 275 W");
    }

    private static PowerSnapshotHistoryPoint[] Points() =>
    [
        new() { RecordedAtUtc = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc), PvPowerW = 300, LoadPowerW = 120 },
        new() { RecordedAtUtc = new DateTime(2026, 6, 17, 12, 5, 0, DateTimeKind.Utc), PvPowerW = 450, LoadPowerW = 275 },
    ];
}

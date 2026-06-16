using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Components.Pages;

namespace HVO.Hardware.DavisVantagePro2.Tests.Components;

[TestClass]
public class DavisAstronomicalChartBuilderTests
{
    [TestMethod]
    public void Build_WithSunriseSunset_BuildsSunArcAndCurrentMarker()
    {
        var model = DavisAstronomicalChartBuilder.Build(
            new DateTime(2026, 6, 16, 12, 0, 0),
            "06:00",
            "18:00",
            moonriseText: null,
            moonsetText: null);

        model.Labels.Should().HaveCount(48);
        model.Datasets.Should().HaveCount(2);
        model.Datasets[0].Data[24].Should().Be(100);
        model.Datasets[1].Data[24].Should().Be(100);
    }

    [TestMethod]
    public void Build_WithOvernightMoonrise_BuildsMoonArcAcrossMidnight()
    {
        var model = DavisAstronomicalChartBuilder.Build(
            new DateTime(2026, 6, 16, 23, 0, 0),
            "06:00",
            "18:00",
            "10:00 PM",
            "2:00 AM");

        model.Datasets.Should().HaveCount(4);
        model.Datasets[2].Data[44].Should().Be(0);
        model.Datasets[2].Data[0].Should().BeGreaterThan(0);
        model.Datasets[2].Data[4].Should().Be(0);
        model.Datasets[3].Data[46].Should().NotBeNull();
    }
}

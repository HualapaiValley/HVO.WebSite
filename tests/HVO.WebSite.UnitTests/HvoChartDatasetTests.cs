using FluentAssertions;
using HVO.WebSite.Themes.Components.Charts;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HvoChartDatasetTests
{
    [TestMethod]
    public void Constructor_SetsProperties()
    {
        var data = new double[] { 1.0, 2.0, 3.0 };
        var ds = new HvoChartDataset("Test", data, "#ff0000", "#00ff00", 2, 5, true);

        ds.Label.Should().Be("Test");
        ds.Data.Should().Equal(data);
        ds.BorderColor.Should().Be("#ff0000");
        ds.BackgroundColor.Should().Be("#00ff00");
        ds.BorderWidth.Should().Be(2);
        ds.PointRadius.Should().Be(5);
        ds.Fill.Should().BeTrue();
    }

    [TestMethod]
    public void Constructor_Defaults()
    {
        var ds = new HvoChartDataset("Default", new double[] { 1.0 });
        ds.BorderColor.Should().BeNull();
        ds.BackgroundColor.Should().BeNull();
        ds.BorderWidth.Should().Be(3);
        ds.PointRadius.Should().Be(3);
        ds.Fill.Should().BeFalse();
    }
}

using FluentAssertions;
using HVO.WebSite.Themes.Components.Charts;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HvoChartDatasetTests
{
    [TestMethod]
    public void Constructor_SetsProperties()
    {
        var data = new double?[] { 1.0, 2.0, null, 3.0 };
        var ds = new HvoChartDataset("Test", data, "#ff0000", "#00ff00", 2, 5, true, 0.3);

        ds.Label.Should().Be("Test");
        ds.Data.Should().Equal(data);
        ds.BorderColor.Should().Be("#ff0000");
        ds.BackgroundColor.Should().Be("#00ff00");
        ds.BorderWidth.Should().Be(2);
        ds.PointRadius.Should().Be(5);
        ds.Fill.Should().BeTrue();
        ds.Tension.Should().Be(0.3);
    }

    [TestMethod]
    public void Constructor_Defaults()
    {
        var ds = new HvoChartDataset("Default", new double?[] { 1.0, null, 3.0 });
        ds.BorderColor.Should().BeNull();
        ds.BackgroundColor.Should().BeNull();
        ds.BorderWidth.Should().Be(3);
        ds.PointRadius.Should().Be(3);
        ds.Fill.Should().BeFalse();
        ds.Tension.Should().BeNull();
    }

    [TestMethod]
    public void Data_AllowsNullEntries_RepresentingGaps()
    {
        var sparseData = new double?[] { 1.0, null, null, 4.0, null, 6.0 };
        var ds = new HvoChartDataset("Sparse", sparseData);

        ds.Data.Should().HaveCount(6);
        ds.Data[0].Should().Be(1.0);
        ds.Data[1].Should().BeNull();
        ds.Data[2].Should().BeNull();
        ds.Data[5].Should().Be(6.0);
    }
}

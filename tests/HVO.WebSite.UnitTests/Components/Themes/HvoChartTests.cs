using System.Text.Json;
using Bunit;
using FluentAssertions;
using HVO.WebSite.Themes.Components.Charts;
using Microsoft.JSInterop;

namespace HVO.WebSite.UnitTests.Components.Themes;

[TestClass]
public sealed class HvoChartTests : BunitContext
{
    [TestMethod]
    public void InitialRenderInvokesJsWithExpectedConfig()
    {
        JSInterop.SetupVoid("hvoChart.render", _ => true);

        Render<HvoChart>(parameters => parameters
            .Add(p => p.ChartId, "temperature-chart")
            .Add(p => p.Type, HvoChartType.Line)
            .Add(p => p.Labels, ["00:00", "00:05", "00:10"])
            .Add(p => p.Datasets, [new HvoChartDataset("Temperature", [1.2, null, 3.4], "#69d3ff", "#69d3ff", Tension: 0.2)])
            .Add(p => p.Title, "Temperature")
            .Add(p => p.XAxisLabel, "Time")
            .Add(p => p.YAxisLabel, "Degrees")
            .Add(p => p.YAxisSuggestedMin, 0d)
            .Add(p => p.YAxisSuggestedMax, 10d));

        JSInterop.Invocations["hvoChart.render"].Should().ContainSingle();
        var invocation = JSInterop.Invocations["hvoChart.render"].Single();
        invocation.Identifier.Should().Be("hvoChart.render");
        invocation.Arguments[0].Should().Be("temperature-chart");
        var configJson = JsonSerializer.Serialize(invocation.Arguments[1]);
        configJson.Should().Contain("\"type\":\"line\"");
        configJson.Should().Contain("Temperature");
        configJson.Should().Contain("[1.2,null,3.4]");
        configJson.Should().Contain("\"spanGaps\":false");
        configJson.Should().Contain("\"tension\":0.2");
        configJson.Should().Contain("\"suggestedMin\":0");
        configJson.Should().Contain("\"suggestedMax\":10");
    }

    [TestMethod]
    public void DataRevisionChangeRerendersChart()
    {
        JSInterop.SetupVoid("hvoChart.render", _ => true);
        var component = Render<HvoChart>(parameters => parameters
            .Add(p => p.ChartId, "power-chart")
            .Add(p => p.Labels, ["00:00"])
            .Add(p => p.Datasets, [new HvoChartDataset("Power", [1.0])])
            .Add(p => p.DataRevision, 1));

        component.Render(parameters => parameters
            .Add(p => p.Labels, ["00:00", "00:05"])
            .Add(p => p.Datasets, [new HvoChartDataset("Power", [1.0, 2.0])])
            .Add(p => p.DataRevision, 2));

        JSInterop.Invocations["hvoChart.render"].Should().HaveCount(2);
        JSInterop.Invocations["hvoChart.render"].Select(invocation => invocation.Arguments[0]).Should().AllBeEquivalentTo("power-chart");
    }

    [TestMethod]
    public void JsExceptionIsSwallowed()
    {
        JSInterop.SetupVoid("hvoChart.render", _ => true).SetException(new JSException("Chart failed"));

        var act = () => Render<HvoChart>(parameters => parameters
            .Add(p => p.ChartId, "safe-chart")
            .Add(p => p.Datasets, [new HvoChartDataset("Safe", [1.0])]))
            .Markup;

        act.Should().NotThrow();
        JSInterop.Invocations["hvoChart.render"].Should().ContainSingle();
    }
}

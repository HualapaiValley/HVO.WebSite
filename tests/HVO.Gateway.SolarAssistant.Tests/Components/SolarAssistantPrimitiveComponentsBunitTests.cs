using Bunit;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Components.Pages;
using MudBlazor.Services;

namespace HVO.Gateway.SolarAssistant.Tests.Components;

[TestClass]
public sealed class SolarAssistantPrimitiveComponentsBunitTests : BunitContext
{
    public SolarAssistantPrimitiveComponentsBunitTests() => Services.AddMudServices();

    [TestMethod]
    public void CardHead_RendersOptionalLink()
    {
        var component = Render<CardHead>(parameters => parameters
            .Add(p => p.Eyebrow, "Discovery")
            .Add(p => p.Title, "REST Topics")
            .Add(p => p.LinkHref, "/inventory")
            .Add(p => p.LinkText, "JSON"));

        component.Markup.Should().Contain("Discovery");
        component.Markup.Should().Contain("REST Topics");
        component.Find("a[href='/inventory']").TextContent.Should().Contain("JSON");
    }

    [TestMethod]
    public void DetailRow_RendersLabelAndValue()
    {
        var component = Render<DetailRow>(parameters => parameters
            .Add(p => p.Label, "Host")
            .Add(p => p.Value, "solar.local"));

        component.Markup.Should().Contain("Host");
        component.Find("strong").TextContent.Should().Be("solar.local");
        component.Find("strong").GetAttribute("title").Should().Be("solar.local");
    }

    [TestMethod]
    public void MetricTile_RendersLabelValueAndDelta()
    {
        var component = Render<MetricTile>(parameters => parameters
            .Add(p => p.Label, "PV")
            .Add(p => p.Value, "3,098 W"));

        component.Markup.Should().Contain("PV");
        component.Find("strong").TextContent.Should().Be("3,098 W");
    }
}

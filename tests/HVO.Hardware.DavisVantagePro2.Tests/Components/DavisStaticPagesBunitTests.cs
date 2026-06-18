using Bunit;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Components.Pages;

namespace HVO.Hardware.DavisVantagePro2.Tests.Components;

[TestClass]
public sealed class DavisStaticPagesBunitTests : BunitContext
{
    [TestMethod]
    public void RendersAlarmActive()
    {
        DavisComponentTestServices.RegisterDavisServices(Services);

        var component = Render<AlarmActive>();

        component.Markup.Should().Contain("Current Alarm State");
        component.Markup.Should().Contain("High wind");
        component.Markup.Should().Contain("No packets");
        component.Markup.Should().Contain("Acknowledge");
    }

    [TestMethod]
    public void RendersAlarmDefinitions()
    {
        DavisComponentTestServices.RegisterDavisServices(Services);

        var component = Render<AlarmDefinitions>();

        component.Markup.Should().Contain("Definitions Workspace");
        component.Markup.Should().Contain("Alarm Sources");
        component.Markup.Should().Contain("High Wind");
        component.Markup.Should().Contain("Current Console Threshold Values");
    }

    [TestMethod]
    public void RendersStationDiagnostics()
    {
        DavisComponentTestServices.RegisterDavisServices(Services);

        var component = Render<StationDiagnostics>();

        component.Markup.Should().Contain("Hardware Profile");
        component.Markup.Should().Contain("Reception Stats");
        component.Markup.Should().Contain("Heard Channels");
        component.Markup.Should().Contain("Barometer Data");
    }
}

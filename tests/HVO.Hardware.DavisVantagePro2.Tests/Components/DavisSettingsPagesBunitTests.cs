using Bunit;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Components.Pages;

namespace HVO.Hardware.DavisVantagePro2.Tests.Components;

[TestClass]
public sealed class DavisSettingsPagesBunitTests : BunitContext
{
    [TestMethod]
    public void RendersConsoleSettings()
    {
        DavisComponentTestServices.RegisterDavisServices(Services);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = Render<ConsoleSettings>();

        component.Markup.Should().Contain("Hardware Configuration");
        component.Markup.Should().Contain("Clock");
        component.Markup.Should().Contain("Location, Time Zone, And Logging");
        component.Markup.Should().Contain("Detected Console Units");
        component.Markup.Should().Contain("Rain And Archive Settings");
    }

    [TestMethod]
    public void RendersStationMetadata()
    {
        DavisComponentTestServices.RegisterDavisServices(Services);

        var component = Render<StationInfo>();

        component.Markup.Should().Contain("Hardware Profile");
        component.Markup.Should().Contain("Vantage Pro 2");
        component.Markup.Should().Contain("3.15");
        component.Markup.Should().Contain("Station Fields");
        component.Markup.Should().Contain("Snapshot cached");
    }
}

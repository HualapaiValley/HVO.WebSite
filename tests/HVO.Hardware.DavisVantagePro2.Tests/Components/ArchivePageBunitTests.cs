using Bunit;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Components.Pages;

namespace HVO.Hardware.DavisVantagePro2.Tests.Components;

[TestClass]
public sealed class ArchivePageBunitTests : BunitContext
{
    [TestMethod]
    public void RendersArchiveTable()
    {
        DavisComponentTestServices.RegisterDavisServices(Services);
        var component = Render<Archive>();

        DavisComponentTestServices.SetField(component, "_historySince", new DateTime(2026, 6, 17, 8, 0, 0));
        DavisComponentTestServices.SetField(component, "_historyRecords", new List<HVO.Hardware.DavisVantagePro2.Protocol.Packets.ArchiveRecord>
        {
            DavisComponentTestServices.CreateArchiveRecord(),
        });
        component.Render();

        component.Find("table.hvo-table").Should().NotBeNull();
        component.Markup.Should().Contain("Archive History");
        component.Markup.Should().Contain("1 record loaded");
        component.Markup.Should().Contain("2026-06-17 08:30");
        component.Markup.Should().Contain("29.981");
    }

    [TestMethod]
    public void RendersEmptyArchiveState()
    {
        DavisComponentTestServices.RegisterDavisServices(Services);
        var component = Render<Archive>();

        DavisComponentTestServices.SetField(component, "_historySince", new DateTime(2026, 6, 17, 8, 0, 0));
        DavisComponentTestServices.SetField(component, "_historyRecords", new List<HVO.Hardware.DavisVantagePro2.Protocol.Packets.ArchiveRecord>());
        component.Render();

        component.Markup.Should().Contain("0 records loaded");
        component.Markup.Should().Contain("No records found for the selected time range.");
    }
}

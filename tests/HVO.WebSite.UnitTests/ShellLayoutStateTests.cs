using FluentAssertions;
using HVO.WebSite.Themes.Components.Layout;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class ShellLayoutStateTests
{
    [TestMethod]
    public void Defaults_IsDarkMode_True()
    {
        var state = new ShellLayoutState();
        state.IsDarkMode.Should().BeTrue();
    }

    [TestMethod]
    public void SetTheme_ChangesIsDarkMode()
    {
        var state = new ShellLayoutState();
        state.SetTheme(false);
        state.IsDarkMode.Should().BeFalse();
    }

    [TestMethod]
    public void SetTheme_SameValue_DoesNotFireChanged()
    {
        var state = new ShellLayoutState();
        var fired = false;
        state.Changed += () => fired = true;
        state.SetTheme(true); // already true
        fired.Should().BeFalse();
    }

    [TestMethod]
    public void SetTheme_DifferentValue_FiresChanged()
    {
        var state = new ShellLayoutState();
        var fired = false;
        state.Changed += () => fired = true;
        state.SetTheme(false);
        fired.Should().BeTrue();
    }

    [TestMethod]
    public void ToggleTheme_FlipsIsDarkMode()
    {
        var state = new ShellLayoutState();
        var initial = state.IsDarkMode;
        state.ToggleTheme();
        state.IsDarkMode.Should().Be(!initial);
    }

    [TestMethod]
    public void SetFooter_UpdatesSlots()
    {
        var state = new ShellLayoutState();
        state.SetFooter(
            new ShellFooterItem("A", ShellFooterIndicator.Online),
            new ShellFooterItem("B"),
            new ShellFooterItem("C", ShellFooterIndicator.Warning),
            new ShellFooterItem("D"),
            new ShellFooterItem("E", ShellFooterIndicator.Offline));

        state.FooterSlot1.Text.Should().Be("A");
        state.FooterSlot1.Indicator.Should().Be(ShellFooterIndicator.Online);
        state.FooterSlot2.Text.Should().Be("B");
        state.FooterSlot3.Indicator.Should().Be(ShellFooterIndicator.Warning);
        state.FooterSlot5.Indicator.Should().Be(ShellFooterIndicator.Offline);
    }

    [TestMethod]
    public void SetFooter_NoChange_DoesNotFireChanged()
    {
        var state = new ShellLayoutState();
        var fired = false;
        state.Changed += () => fired = true;
        state.SetFooter(
            state.FooterSlot1,
            state.FooterSlot2,
            state.FooterSlot3,
            state.FooterSlot4,
            state.FooterSlot5);
        fired.Should().BeFalse();
    }

    [TestMethod]
    public void SetFooter_Change_FiresChanged()
    {
        var state = new ShellLayoutState();
        var fired = false;
        state.Changed += () => fired = true;
        state.SetFooter(
            new ShellFooterItem("New"),
            state.FooterSlot2,
            state.FooterSlot3,
            state.FooterSlot4,
            state.FooterSlot5);
        fired.Should().BeTrue();
    }

    [TestMethod]
    public void ResetFooter_RestoresDefaults()
    {
        var state = new ShellLayoutState();
        state.SetFooter(
            new ShellFooterItem("Custom1"),
            new ShellFooterItem("Custom2"),
            new ShellFooterItem("Custom3"),
            new ShellFooterItem("Custom4"),
            new ShellFooterItem("Custom5"));
        state.ResetFooter();
        state.FooterSlot1.Text.Should().Be("Status");
        state.FooterSlot2.Text.Should().Be("Gateway");
    }

    [TestMethod]
    public void ShellFooterItem_DefaultsToNoneIndicator()
    {
        var item = new ShellFooterItem("Test");
        item.Indicator.Should().Be(ShellFooterIndicator.None);
    }

    [TestMethod]
    public void SetPage_UpdatesProperties()
    {
        var state = new ShellLayoutState();
        state.SetPage("Alerts", "Alert Dashboard", "View active alerts");
        state.CurrentSection.Should().Be("Alerts");
        state.PageTitle.Should().Be("Alert Dashboard");
        state.PageSummary.Should().Be("View active alerts");
    }
}

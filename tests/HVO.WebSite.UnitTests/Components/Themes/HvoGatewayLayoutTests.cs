using Bunit;
using FluentAssertions;
using HVO.WebSite.Themes.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HVO.WebSite.UnitTests.Components.Themes;

[TestClass]
public sealed class HvoGatewayLayoutTests : BunitContext
{
    public HvoGatewayLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [TestMethod]
    public void RendersBrandSubtitleNavActionsFooterSlots()
    {
        var component = Render<HvoGatewayLayout>(parameters => parameters
            .Add(p => p.Subtitle, "TEST GATEWAY")
            .Add(p => p.NavItems, Markup("<a href=\"/status\">Status</a>"))
            .Add(p => p.AppBarActions, Markup("<button type=\"button\">Refresh</button>"))
            .Add(p => p.FooterSlot1, new ShellFooterItem("Online", ShellFooterIndicator.Online))
            .Add(p => p.FooterSlot2, new ShellFooterItem("Davis"))
            .Add(p => p.FooterSlot3, new ShellFooterItem("Ready"))
            .Add(p => p.FooterSlot4, new ShellFooterItem("Telemetry"))
            .Add(p => p.FooterSlot5, new ShellFooterItem("Connected", ShellFooterIndicator.Online))
            .AddChildContent("<main>Gateway body</main>"));

        component.Markup.Should().Contain("Hualapai Valley Observatory");
        component.Markup.Should().Contain("TEST GATEWAY");
        component.Markup.Should().Contain("Status");
        component.Markup.Should().Contain("Refresh");
        component.Markup.Should().Contain("Gateway body");
        component.Markup.Should().Contain("Online");
        component.Markup.Should().Contain("Davis");
        component.Markup.Should().Contain("Ready");
        component.Markup.Should().Contain("Telemetry");
        component.Markup.Should().Contain("Connected");
    }

    [TestMethod]
    public void AppliesDarkAndLightShellThemeClass()
    {
        var dark = Render<HvoGatewayLayout>(parameters => parameters
            .Add(p => p.IsDarkMode, true)
            .AddChildContent("Dark body"));
        dark.Find(".mud-layout").ClassList.Should().Contain("shell-theme-dark");

        dark.Render(parameters => parameters
            .Add(p => p.IsDarkMode, false)
            .AddChildContent("Light body"));
        dark.Find(".mud-layout").ClassList.Should().Contain("shell-theme-light");
    }

    [TestMethod]
    public void RendersFooterIndicators()
    {
        var component = Render<HvoGatewayLayout>(parameters => parameters
            .Add(p => p.FooterSlot1, new ShellFooterItem("Offline", ShellFooterIndicator.Offline))
            .Add(p => p.FooterSlot5, new ShellFooterItem("Warning", ShellFooterIndicator.Warning))
            .AddChildContent("Gateway body"));

        component.Find(".shell-footer-slot-1 .shell-status-dot-offline").Should().NotBeNull();
        component.Find(".shell-footer-slot-5 .shell-status-dot-warning").Should().NotBeNull();
        component.Markup.Should().Contain("Offline");
        component.Markup.Should().Contain("Warning");
    }

    private static RenderFragment Markup(string markup) => builder => builder.AddMarkupContent(0, markup);
}

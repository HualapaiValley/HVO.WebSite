using Bunit;
using FluentAssertions;
using HVO.WebSite.Themes.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HVO.WebSite.UnitTests.Components.Themes;

[TestClass]
public sealed class HvoPublicLayoutTests : BunitContext
{
    public HvoPublicLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [TestMethod]
    public void RendersBrandNavAuthAndBody()
    {
        var component = Render<HvoPublicLayout>(parameters => parameters
            .Add(p => p.BrandTitle, "HVO Public")
            .Add(p => p.BrandSubtitle, "OBSERVATORY")
            .Add(p => p.NavItems, Markup("<a href=\"/weather\">Weather</a>"))
            .Add(p => p.AuthSection, Markup("<a href=\"/login\">Login</a>"))
            .AddChildContent("<section>Public body</section>"));

        component.Markup.Should().Contain("HVO Public");
        component.Markup.Should().Contain("OBSERVATORY");
        component.Markup.Should().Contain("Weather");
        component.Markup.Should().Contain("Login");
        component.Markup.Should().Contain("Public body");
    }

    [TestMethod]
    public void HidesSubtitleWhenBlank()
    {
        var component = Render<HvoPublicLayout>(parameters => parameters
            .Add(p => p.BrandTitle, "HVO Public")
            .Add(p => p.BrandSubtitle, " ")
            .AddChildContent("Public body"));

        component.Markup.Should().NotContain("shell-brand-subtitle");
        component.Markup.Should().Contain("HVO Public");
    }

    [TestMethod]
    public void RendersBlazorErrorUi()
    {
        var component = Render<HvoPublicLayout>(parameters => parameters
            .AddChildContent("Public body"));

        component.Find("#blazor-error-ui").TextContent.Should().Contain("Blazor circuit interrupted");
        component.Find("#components-reconnect-modal").Should().NotBeNull();
    }

    private static RenderFragment Markup(string markup) => builder => builder.AddMarkupContent(0, markup);
}

using Bunit;
using FluentAssertions;
using HVO.WebSite.Themes.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HVO.WebSite.UnitTests.Components.Themes;

[TestClass]
public sealed class HvoAdminLayoutTests : BunitContext
{
    public HvoAdminLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [TestMethod]
    public void RendersSidebarActionsAndBody()
    {
        var component = Render<HvoAdminLayout>(parameters => parameters
            .Add(p => p.SidebarItems, Markup("<a href=\"/admin/users\">Users</a>"))
            .Add(p => p.AppBarActions, Markup("<button type=\"button\">Sign out</button>"))
            .AddChildContent("<section>Admin body</section>"));

        component.Markup.Should().Contain("Hualapai Valley Observatory");
        component.Markup.Should().Contain("ADMIN");
        component.Markup.Should().Contain("Navigation");
        component.Markup.Should().Contain("Users");
        component.Markup.Should().Contain("Sign out");
        component.Markup.Should().Contain("Admin body");
    }

    [TestMethod]
    public void DrawerToggleChangesDrawerState()
    {
        var component = Render<HvoAdminLayout>(parameters => parameters
            .Add(p => p.SidebarItems, Markup("<a href=\"/admin/users\">Users</a>"))
            .AddChildContent("Admin body"));
        component.Markup.Should().Contain("mud-drawer-open");

        component.Find("button").Click();

        component.Markup.Should().Contain("mud-drawer-close");
    }

    private static RenderFragment Markup(string markup) => builder => builder.AddMarkupContent(0, markup);
}

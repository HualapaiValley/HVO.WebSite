using FluentAssertions;
using HVO.WebSite.PlaywrightTests.Infrastructure;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Models;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
[TestCategory("Browser")]
public sealed class WebsiteDashboardBrowserTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AnonymousShellTogglesActualThemeAndServesRealAssets()
    {
        await using var application = new WebsiteBrowserApplication();
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await page.GotoAsync(application.Address.ToString());
        await Assertions.Expect(page.Locator(".power-unauthenticated-card")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
        await ToggleThemeAsync(page);
        var stylesheet = await page.Context.APIRequest.GetAsync(new Uri(application.Address, "_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css").ToString());
        stylesheet.Status.Should().Be(200);
        (await stylesheet.TextAsync()).Should().Contain("--shell-card-background");
        var bootstrap = await page.Context.APIRequest.GetAsync(new Uri(application.Address, "_framework/blazor.web.js").ToString());
        bootstrap.Status.Should().Be(200);
        await AssertHealthyAsync(browser);
        await page.RouteAsync("https://login.microsoftonline.com/**", route => route.FulfillAsync(new()
        { Status = 200, ContentType = "text/plain", Body = "Identity provider navigation intercepted by the test" }));
        await page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("https://login.microsoftonline.com/**");
        page.Url.Should().Contain("redirect_uri=");
        browser.AssertNoUnexpectedErrors();
    }

    [TestMethod]
    public async Task AccountMenuAdminDrawerAndNavigationRunInTheInteractiveOwner()
    {
        await using var application = new WebsiteBrowserApplication();
        application.Power.Data = WebsitePowerState.Populated(application.Clock.GetUtcNow().UtcDateTime);
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await SetRoleAsync(page, AppRoles.Admin);
        await page.GotoAsync(new Uri(application.Address, "/admin?view=dashboard#top").ToString());
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Administration" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Browser User" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        var drawer = page.Locator(".mud-drawer");
        await Assertions.Expect(drawer).ToHaveClassAsync(new Regex("mud-drawer--open"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle admin navigation" }).ClickAsync();
        await Assertions.Expect(drawer).ToHaveClassAsync(new Regex("mud-drawer--closed"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle admin navigation" }).ClickAsync();
        await Assertions.Expect(drawer).ToHaveClassAsync(new Regex("mud-drawer--open"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-light")).ToBeVisibleAsync();
        await page.EvaluateAsync("window.hvoNavigationMarker = 'same-document'");
        await page.GetByRole(AriaRole.Link, new() { Name = "Back to Site" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Home", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-theme-light")).ToBeVisibleAsync();
        (await page.EvaluateAsync<string>("window.hvoNavigationMarker")).Should().Be("same-document");
        await Assertions.Expect(page.Locator(".power-metric--battery strong")).ToHaveTextAsync("+500 W");
        await AssertHealthyAsync(browser);
    }

    [TestMethod]
    public async Task AnonymousAdminRequestIsRejectedByTheRealOidcChallenge()
    {
        await using var application = new WebsiteBrowserApplication();
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        // The actual challenge redirect is followed by the browser, but no external IdP is contacted.
        await page.RouteAsync("https://login.microsoftonline.com/**", route => route.FulfillAsync(new()
        { Status = 200, ContentType = "text/plain", Body = "Identity provider navigation intercepted by the test" }));
        await page.GotoAsync(new Uri(application.Address, "/admin").ToString());
        page.Url.Should().StartWith("https://login.microsoftonline.com/");
        page.Url.Should().Contain("redirect_uri=");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Administration" })).ToHaveCountAsync(0);
        application.Power.CurrentCalls.Should().Be(0);
        browser.AssertNoUnexpectedErrors();
    }

    [TestMethod]
    public async Task NonAdminRoleIsRejectedAndRolelessPrincipalCannotReadPower()
    {
        await using var application = new WebsiteBrowserApplication();
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await SetRoleAsync(page, AppRoles.User);
        await page.GotoAsync(new Uri(application.Address, "/admin").ToString());
        page.Url.Should().Contain("AccessDenied");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Administration" })).ToHaveCountAsync(0);
        await SetRoleAsync(page, "");
        await page.GotoAsync(application.Address.ToString());
        await Assertions.Expect(page.Locator(".power-unauthenticated-card")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Browser User" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign out" })).ToBeVisibleAsync();
        application.Power.CurrentCalls.Should().Be(0);
        await AssertHealthyAsync(browser, "Failed to load resource: the server responded with a status of 404");
    }

    [TestMethod]
    public async Task WaitingDashboardFindsNewDataAndFetchesHistoryOnItsOwnCadence()
    {
        await using var application = new WebsiteBrowserApplication();
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await SetRoleAsync(page, AppRoles.User);
        await page.GotoAsync(application.Address.ToString());
        await Assertions.Expect(page.GetByText("Waiting for power telemetry. This dashboard refreshes automatically.", new() { Exact = true })).ToBeVisibleAsync();
        application.Power.CurrentCalls.Should().Be(1, "SSR should render loading, and the interactive owner should query once");
        application.Power.HistoryCalls.Should().Be(1);
        application.Power.Data = WebsitePowerState.Populated(application.Clock.GetUtcNow().UtcDateTime);
        application.Clock.Advance(TimeSpan.FromSeconds(5));
        await Assertions.Expect(page.Locator(".power-metric--battery strong")).ToHaveTextAsync("+500 W");
        application.Power.CurrentCalls.Should().Be(2);
        application.Power.HistoryCalls.Should().Be(1);
        await Assertions.Expect(page.Locator("#site-pv-history-chart")).ToHaveCountAsync(0);
        application.Clock.Advance(TimeSpan.FromSeconds(55));
        await Assertions.Expect(page.Locator("#site-pv-history-chart")).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("Boolean(window.Chart?.getChart('site-pv-history-chart'))");
        application.Power.HistoryCalls.Should().Be(2);
        await Assertions.Expect(page.Locator(".power-status-card__header > .hvo-chip")).ToHaveTextAsync("Stale");
        application.Power.Data = WebsitePowerState.Populated(application.Clock.GetUtcNow().UtcDateTime, alarm: true);
        application.Clock.Advance(TimeSpan.FromSeconds(5));
        await Assertions.Expect(page.Locator(".power-status-card__header > .hvo-chip")).ToHaveTextAsync("Live");
        await Assertions.Expect(page.GetByText("Active alarm", new() { Exact = true })).ToBeVisibleAsync();
        application.Power.HistoryCalls.Should().Be(2);
        await ToggleThemeAsync(page);
        await AssertHealthyAsync(browser);
    }

    [TestMethod]
    [DataRow("current")]
    [DataRow("inverter")]
    [DataRow("controller")]
    [DataRow("history")]
    public async Task InitialAndRefreshProviderFailuresPreserveCircuitAndOtherSections_ThenRecover(string failingSection)
    {
        await using var application = new WebsiteBrowserApplication();
        application.Power.Data = WebsitePowerState.Populated(application.Clock.GetUtcNow().UtcDateTime) with { Failure = failingSection };
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await SetRoleAsync(page, AppRoles.User);
        await page.GotoAsync(application.Address.ToString());
        var error = page.Locator($"[data-section-error='{failingSection}']");
        await Assertions.Expect(error).ToBeVisibleAsync();
        if (failingSection != "current") await Assertions.Expect(page.Locator(".power-metric--battery strong")).ToHaveTextAsync("+500 W");
        if (failingSection != "inverter") await Assertions.Expect(page.Locator(".power-eg4-detail__grid > article:first-child dl").GetByText("1234 W", new() { Exact = true })).ToBeVisibleAsync();
        if (failingSection != "history") await Assertions.Expect(page.Locator("#site-pv-history-chart")).ToBeVisibleAsync();
        await ToggleThemeAsync(page);
        await AssertHealthyAsync(browser);
        application.Power.Data = WebsitePowerState.Populated(application.Clock.GetUtcNow().UtcDateTime);
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry power telemetry" }).ClickAsync();
        await Assertions.Expect(page.Locator("[data-section-error]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".power-metric--battery strong")).ToHaveTextAsync("+500 W");
        application.Power.Data = application.Power.Data with { Failure = failingSection };
        application.Clock.Advance(TimeSpan.FromSeconds(60));
        await Assertions.Expect(error).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".power-metric--battery strong")).ToHaveTextAsync("+500 W");
        await Assertions.Expect(page.Locator("#site-pv-history-chart")).ToBeVisibleAsync();
        await ToggleThemeAsync(page);
        application.Power.Data = WebsitePowerState.Populated(application.Clock.GetUtcNow().UtcDateTime, alarm: true);
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry power telemetry" }).ClickAsync();
        await Assertions.Expect(page.Locator("[data-section-error]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByText("Active alarm", new() { Exact = true })).ToBeVisibleAsync();
        await AssertHealthyAsync(browser);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnexpectedDashboardRenderFailureIsContainedAndCanBeReloaded(bool afterRefresh)
    {
        await using var application = new WebsiteBrowserApplication();
        var data = WebsitePowerState.Populated(application.Clock.GetUtcNow().UtcDateTime);
        var malformed = data with { Snapshot = data.Snapshot! with { BatteryObservations = [null!] } };
        application.Power.Data = afterRefresh ? data : malformed;
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await SetRoleAsync(page, AppRoles.User);
        await page.GotoAsync(application.Address.ToString());
        if (afterRefresh)
        {
            await Assertions.Expect(page.Locator(".power-metric--battery strong")).ToHaveTextAsync("+500 W");
            application.Power.Data = malformed;
            application.Clock.Advance(TimeSpan.FromSeconds(5));
        }
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reload dashboard" })).ToBeVisibleAsync();
        await ToggleThemeAsync(page);
        application.Power.Data = data;
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload dashboard" }).ClickAsync();
        await Assertions.Expect(page.Locator(".power-metric--battery strong")).ToHaveTextAsync("+500 W");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reload dashboard" })).ToHaveCountAsync(0);
        await AssertHealthyAsync(browser);
    }

    private static Task SetRoleAsync(IPage page, string role) => page.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Role"] = role });
    private static async Task ToggleThemeAsync(IPage page)
    {
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-light")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
    }
    private static async Task AssertHealthyAsync(BrowserSession browser, string? allowedError = null)
    {
        await Assertions.Expect(browser.Page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        browser.AssertNoUnexpectedErrors(allowedError);
    }
}

using FluentAssertions;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class MainSitePlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task MainSite_ShouldRenderPublicHomeShell()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Home" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Current observatory power telemetry")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Home" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task MainSite_ShouldShowUnauthenticatedPowerCardMessage()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));

        await Assertions.Expect(page.Locator(".power-unauthenticated-card")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Live Power Snapshot" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Sign in with observatory access to view the live power-system card.")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task MainSite_AdminRoute_ShouldRedirectUnauthenticatedUserToLogin()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/admin"));

        await Assertions.Expect(page).ToHaveURLAsync(new Regex("MicrosoftIdentity/Account/SignIn", RegexOptions.IgnoreCase));
        page.Url.Should().Contain("redirectUri");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task MainSite_ShouldToggleThemeWithoutCircuitError()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-light")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task MainSite_ShouldRenderSharedThemeCss()
    {
        var requestedUrls = new List<string>();
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;
        page.Request += (_, request) => requestedUrls.Add(request.Url);

        await page.GotoAsync(BuildUrl("/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-components.css", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(page.Locator(".hvo-card").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-eyebrow").First).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    private static async Task<(IPlaywright Playwright, IBrowser Browser, IPage Page)> CreatePageAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_WEBSITE_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_WEBSITE_BASE_URL to run the main site UI Playwright tests.");
        }

        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync(new BrowserNewPageOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
            });
            return (playwright, browser, page);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    private static string BuildUrl(string route)
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_WEBSITE_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_WEBSITE_BASE_URL to run the main site UI Playwright tests.");
        }

        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), route.TrimStart('/')).ToString();
    }

    private static async Task AssertNoBlazorErrorAsync(IPage page) =>
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
}

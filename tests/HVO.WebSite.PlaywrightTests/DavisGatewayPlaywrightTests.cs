using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class DavisGatewayPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task DavisGateway_ShouldRenderLayoutShell()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_DAVIS_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            Assert.Inconclusive("Set HVO_DAVIS_BASE_URL to run the Davis gateway UI Playwright test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(baseUrl);

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("DAVIS WEATHER DASHBOARD");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Overview" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Alerts" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Archive" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Configuration" })).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task DavisGateway_ShouldRenderStatusChartsAndKeepCircuitAlive()
    {
        var baseUrl = GetBaseUrl();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(BuildUrl(baseUrl, "/"));

        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);
        await PlaywrightGatewayAssertions.AssertChartRenderedAsync(page, "status-temp-chart");
        await PlaywrightGatewayAssertions.AssertChartRenderedAsync(page, "status-wind-chart");
        await PlaywrightGatewayAssertions.AssertChartRenderedAsync(page, "status-solar-chart");
        await PlaywrightGatewayAssertions.AssertChartRenderedAsync(page, "status-astro-chart");
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task DavisGateway_ShouldLoadOnlyLocalOfflineResources()
    {
        var baseUrl = GetBaseUrl();
        var requestedUrls = new List<string>();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        page.Request += (_, request) => requestedUrls.Add(request.Url);

        await page.GotoAsync(BuildUrl(baseUrl, "/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await PlaywrightGatewayAssertions.AssertNoCdnResourcesAsync(requestedUrls);
        await PlaywrightGatewayAssertions.AssertLocalThemeResourcesLoadedAsync(requestedUrls);
        await PlaywrightGatewayAssertions.AssertLocalChartResourcesLoadedAsync(requestedUrls);
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task DavisGateway_ShouldKeepCircuitAliveAcrossTopNavigation()
    {
        var baseUrl = GetBaseUrl();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(BuildUrl(baseUrl, "/"));
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Alerts" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/alarms/?$"));
        await Assertions.Expect(page.Locator(".alarm-definitions-page")).ToBeVisibleAsync();
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Archive" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/archive/?$"));
        await Assertions.Expect(page.Locator(".archive-page")).ToBeVisibleAsync();
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Configuration" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/console-settings/?$"));
        await Assertions.Expect(page.Locator(".console-settings-page")).ToBeVisibleAsync();
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Overview" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/($|monitor/?$)"));
        await Assertions.Expect(page.GetByText("Last 24 Hours", new() { Exact = true })).ToBeVisibleAsync();
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);
    }

    private static string GetBaseUrl()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_DAVIS_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            Assert.Inconclusive("Set HVO_DAVIS_BASE_URL to run the Davis gateway UI Playwright test.");

        return baseUrl;
    }

    private static string BuildUrl(string baseUrl, string route) =>
        new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), route.TrimStart('/')).ToString();
}

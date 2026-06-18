using FluentAssertions;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class SmartShuntGatewayPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task SmartShuntGateway_ShouldRenderLayoutShell()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_SMARTSHUNT_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            Assert.Inconclusive("Set HVO_SMARTSHUNT_BASE_URL to run the SmartShunt gateway UI Playwright test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(baseUrl);

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("VICTRON SMARTSHUNT DASHBOARD");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Overview" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Telemetry" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Telemetry" }).ClickAsync();
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#smartshunt-telemetry-chart")).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => Boolean(window.hvoChart?._instances?.['smartshunt-telemetry-chart'])");
        var chartWidth = await page.Locator("#smartshunt-telemetry-chart").EvaluateAsync<int>("canvas => canvas.clientWidth");
        chartWidth.Should().BeGreaterThan(0);
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SmartShuntGateway_ShouldLoadOnlyLocalOfflineResources()
    {
        var requestedUrls = new List<string>();
        await using var session = await CreateBrowserSessionAsync();
        var page = await session.Browser.NewPageAsync();
        page.Request += (_, request) => requestedUrls.Add(request.Url);

        await page.GotoAsync(BuildUrl("/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await PlaywrightGatewayAssertions.AssertNoCdnResourcesAsync(requestedUrls);
        await PlaywrightGatewayAssertions.AssertLocalChartResourcesLoadedAsync(requestedUrls);
        await PlaywrightGatewayAssertions.AssertLocalThemeResourcesLoadedAsync(requestedUrls);
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SmartShuntGateway_ShouldRenderThemedOverviewCards()
    {
        await using var session = await CreateBrowserSessionAsync();
        var page = await session.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1600, Height = 1000 }
        });

        await page.GotoAsync(BuildUrl("/"));

        await AssertThemedSurfaceAsync(page.Locator(".hvo-card").First, "Overview card");
        await AssertThemedSurfaceAsync(page.Locator(".smartshunt-gauge").First, "Overview gauge");
        await AssertThemedSurfaceAsync(page.Locator(".smartshunt-stat-chip").First, "Overview stat chip");
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SmartShuntGateway_ShouldKeepCircuitAliveAcrossNavigationAndThemeToggle()
    {
        await using var session = await CreateBrowserSessionAsync();
        var page = await session.Browser.NewPageAsync();

        await page.GotoAsync(BuildUrl("/"));
        await Assertions.Expect(page.Locator(".smartshunt-hero-title")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Telemetry" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/telemetry/?$"));
        await Assertions.Expect(page.Locator("#smartshunt-telemetry-chart")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByLabel("Switch to light theme", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-light")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByLabel("Switch to dark theme", new() { Exact = true })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Overview" }).ClickAsync();
        await Assertions.Expect(page.Locator(".smartshunt-hero-title")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SmartShuntGateway_ShouldHaveNoLegacyClasses()
    {
        await using var session = await CreateBrowserSessionAsync();
        var page = await session.Browser.NewPageAsync();

        foreach (var route in new[] { "/", "/telemetry" })
        {
            await page.GotoAsync(BuildUrl(route));
            await AssertNoLegacyClassesAsync(page, route);
            await AssertNoBlazorErrorAsync(page);
        }
    }

    private static async Task<PlaywrightBrowserSession> CreateBrowserSessionAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_SMARTSHUNT_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_SMARTSHUNT_BASE_URL to run the SmartShunt gateway UI Playwright tests.");
        }

        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return new PlaywrightBrowserSession(playwright, browser);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    private static string BuildUrl(string route)
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_SMARTSHUNT_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_SMARTSHUNT_BASE_URL to run the SmartShunt gateway UI Playwright tests.");
        }

        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), route.TrimStart('/')).ToString();
    }

    private static async Task AssertNoBlazorErrorAsync(IPage page) =>
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();

    private static async Task AssertNoLegacyClassesAsync(IPage page, string label)
    {
        var legacyCount = await page.Locator("[class*='proto-'], .action-btn, .card-shell, .archive-table").CountAsync();
        Assert.AreEqual(0, legacyCount, $"{label} should use shared hvo-* theme classes instead of legacy page/prototype classes.");
    }

    private static async Task AssertThemedSurfaceAsync(ILocator locator, string label)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync();
        var style = await GetStyleAsync(locator);

        var hasBackground = !string.Equals(style.BackgroundColor, "rgba(0, 0, 0, 0)", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(style.BackgroundColor, "transparent", StringComparison.OrdinalIgnoreCase);
        var hasBorder = !string.Equals(style.BorderColor, "rgba(0, 0, 0, 0)", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(style.BorderColor, "transparent", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(hasBackground || hasBorder, $"{label} should have themed background or border styling. Style: {style}");
        Assert.IsTrue(ParsePixels(style.BorderRadius) >= 8, $"{label} should have themed rounded styling. Style: {style}");
    }

    private static async Task<ElementStyle> GetStyleAsync(ILocator locator)
    {
        var styleText = await locator.EvaluateAsync<string>(
            "el => { const s = window.getComputedStyle(el); return [s.getPropertyValue('background-color'), s.getPropertyValue('border-color'), s.getPropertyValue('border-radius'), el.getAttribute('class') || el.tagName].join('|'); }");
        var parts = styleText.Split('|');

        return new ElementStyle(
            parts.ElementAtOrDefault(0) ?? string.Empty,
            parts.ElementAtOrDefault(1) ?? string.Empty,
            parts.ElementAtOrDefault(2) ?? string.Empty,
            parts.ElementAtOrDefault(3) ?? string.Empty);
    }

    private static double ParsePixels(string value)
    {
        var firstValue = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0";
        return double.TryParse(firstValue.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), out var pixels) ? pixels : 0;
    }

    private sealed record ElementStyle(string BackgroundColor, string BorderColor, string BorderRadius, string ClassName)
    {
        public override string ToString() => $"background={BackgroundColor}, border={BorderColor}, radius={BorderRadius}, class={ClassName}";
    }

    private sealed class PlaywrightBrowserSession(IPlaywright playwright, IBrowser browser) : IAsyncDisposable
    {
        public IBrowser Browser { get; } = browser;

        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            playwright.Dispose();
        }
    }
}

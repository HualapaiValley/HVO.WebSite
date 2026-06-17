using FluentAssertions;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class SolarAssistantGatewayPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task SolarAssistantGateway_ShouldRenderMonitorShell()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_SOLARASSISTANT_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            Assert.Inconclusive("Set HVO_SOLARASSISTANT_BASE_URL to run the SolarAssistant gateway UI Playwright test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(baseUrl);

        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("SOLARASSISTANT GATEWAY", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "REST JSON" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "MQTT JSON" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "SolarAssistant Gateway" })).ToBeAttachedAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Current Snapshot" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Home Assistant Discovery" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SolarAssistantGateway_ShouldNavigateInventoryRoutesAndKeepCircuitAlive()
    {
        await using var browser = await CreateBrowserAsync();
        var page = await browser.NewPageAsync();

        await page.GotoAsync(BuildUrl("/"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "SolarAssistant Gateway" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "REST JSON" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/inventory/?$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "SolarAssistant REST Topics" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "MQTT JSON" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/mqtt-inventory/?$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Home Assistant Entities" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Monitor" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "SolarAssistant Gateway" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SolarAssistantGateway_ShouldRenderHistoryCharts_WhenHistoryExists()
    {
        await using var browser = await CreateBrowserAsync();
        var page = await browser.NewPageAsync();

        await page.GotoAsync(BuildUrl("/"));

        var charts = page.Locator("canvas[id^='phc-']");
        var chartCount = await charts.CountAsync();
        if (chartCount == 0)
        {
            await Assertions.Expect(page.GetByText("Waiting for history data")).ToHaveCountAsync(4);
        }
        else
        {
            for (var index = 0; index < chartCount; index++)
            {
                var chart = charts.Nth(index);
                await Assertions.Expect(chart).ToBeVisibleAsync();
                var chartWidth = await chart.EvaluateAsync<int>("canvas => canvas.clientWidth");
                chartWidth.Should().BeGreaterThan(0);
            }
        }

        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SolarAssistantGateway_ShouldLoadOnlyLocalOfflineResources()
    {
        var requestedUrls = new List<string>();
        await using var browser = await CreateBrowserAsync();
        var page = await browser.NewPageAsync();
        page.Request += (_, request) => requestedUrls.Add(request.Url);

        await page.GotoAsync(BuildUrl("/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        requestedUrls.Should().NotContain(url =>
            url.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase)
            || url.Contains("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("unpkg.com", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/js/chart.min.js", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/js/hvo-chart.js", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-components.css", StringComparison.OrdinalIgnoreCase));
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SolarAssistantGateway_ShouldRenderThemedCardsAndNoLegacyClasses()
    {
        await using var browser = await CreateBrowserAsync();
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1600, Height = 1000 }
        });

        foreach (var route in new[] { "/", "/inventory", "/mqtt-inventory" })
        {
            await page.GotoAsync(BuildUrl(route));
            await AssertThemedSurfaceAsync(page.Locator(".hvo-card, .solar-card, .solar-hero-card").First, route);
            await AssertNoLegacyClassesAsync(page, route);
            await AssertNoBlazorErrorAsync(page);
        }
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task SolarAssistantGateway_ShouldToggleThemeWithoutCircuitError()
    {
        await using var browser = await CreateBrowserAsync();
        var page = await browser.NewPageAsync();

        await page.GotoAsync(BuildUrl("/"));
        await AssertNoBlazorErrorAsync(page);

        await page.GetByLabel("Switch to light theme", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-light")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByLabel("Switch to dark theme", new() { Exact = true })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByLabel("Switch to dark theme", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByLabel("Switch to light theme", new() { Exact = true })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    private static async Task<IBrowser> CreateBrowserAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_SOLARASSISTANT_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_SOLARASSISTANT_BASE_URL to run the SolarAssistant gateway UI Playwright tests.");
        }

        var playwright = await Playwright.CreateAsync();
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    private static string BuildUrl(string route)
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_SOLARASSISTANT_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_SOLARASSISTANT_BASE_URL to run the SolarAssistant gateway UI Playwright tests.");
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
}

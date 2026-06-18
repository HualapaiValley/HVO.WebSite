using FluentAssertions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class ThemeSandboxPlaywrightTests
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("HVO_THEMESANDBOX_BASE_URL") ?? "http://localhost:5199";

    private static async Task<(IBrowser Browser, IPage Page)> OpenPageAsync(IPlaywright playwright, string path = "/")
    {
        var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1600, Height = 1000 }
        });
        await page.GotoAsync($"{BaseUrl}{path}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return (browser, page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_GatewayLayout_RendersBrandText()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("THEME SANDBOX", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("THEME SANDBOX");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_GatewayLayout_HasFooterSlots()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        var footer = page.Locator(".shell-footer-layout");
        await Assertions.Expect(footer).ToContainTextAsync("Theme Sandbox");
        await Assertions.Expect(footer).ToContainTextAsync("Phase 0.5 validation");
        await Assertions.Expect(footer).ToContainTextAsync("MudBlazor shell");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_GatewayLayout_RendersDarkThemeInitially()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" })).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_FormatPage_ShowsMetricByDefault()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.GetByText("Metric Units")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("22.5 °C")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("18.7 km/h")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("1013.2 hPa")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("13.45 V")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("2.8 A")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("351 W")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("125.5 Ah")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("0.25 in")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("78 %")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("02:30:00")).ToBeVisibleAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Toggle unit system" })).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_HvoChart_RendersCanvas()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        var canvas = page.Locator("#sandbox-chart-dense");
        await Assertions.Expect(canvas).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_NavPills_HighlightActivePage()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        var gatewayPill = page.Locator(".shell-nav-link-current");
        await Assertions.Expect(gatewayPill).ToContainTextAsync("Gateway");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_AdminLayout_HasSidebar()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright, "/admin-layout");
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Admin Panel" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "UI Controls" })).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_PublicLayout_RendersWithBrand()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright, "/public-layout");
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("THEME SANDBOX");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_HvoCss_SharedClassesRender()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.Locator(".hvo-card").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-card-primary").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-card-note").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-metric").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-eyebrow").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-mono").First).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_NullValues_DisplayDashDash()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        var nullEntries = page.GetByText("--");
        var count = await nullEntries.CountAsync();
        Assert.IsTrue(count >= 3, $"Expected at least 3 null entries (--), found {count}");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_Showcase_RendersCardPatterns()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright, "/theme-showcase");
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.Locator(".hvo-ring-gauge").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-cell-grid")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-chip-row")).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_HvoChart_RendersDenseAndSparseCharts()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        await AssertChartRenderedAsync(page, "sandbox-chart-dense");
        await AssertChartRenderedAsync(page, "sandbox-chart-sparse");
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_ShouldKeepCircuitAliveAcrossAllReferenceRoutes()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;

        foreach (var route in new[] { "/", "/dashboard", "/controls", "/instruments", "/css-reference", "/theme-showcase", "/palette", "/states", "/responsive", "/admin-layout", "/public-layout" })
        {
            await page.GotoAsync($"{BaseUrl}{route}");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await AssertNoBlazorErrorAsync(page);
            await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        }
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_ShouldLoadOnlyLocalChartAndThemeResources()
    {
        var requestedUrls = new List<string>();
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright);
        await using var browser = session.Browser;
        var page = session.Page;
        page.Request += (_, request) => requestedUrls.Add(request.Url);

        await page.GotoAsync(BaseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        requestedUrls.Should().NotContain(url =>
            url.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase)
            || url.Contains("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("fonts.gstatic.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("unpkg.com", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/js/chart.min.js", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/js/hvo-chart.js", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-components.css", StringComparison.OrdinalIgnoreCase));
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_CssReference_ShouldValidateControlsAndCardsStyling()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright, "/css-reference");
        await using var browser = session.Browser;
        var page = session.Page;

        await AssertThemedSurfaceAsync(page.Locator(".hvo-card-shell").First, "CSS reference card shell");
        await AssertThemedSurfaceAsync(page.Locator(".hvo-card").First, "CSS reference card");
        await AssertThemedSurfaceAsync(page.Locator(".hvo-control").First, "CSS reference form control");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "hvo-button-primary" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-chip-success")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_Palette_ShouldRenderCanonicalTokens()
    {
        using var playwright = await Playwright.CreateAsync();
        var session = await OpenPageAsync(playwright, "/palette");
        await using var browser = session.Browser;
        var page = session.Page;

        await Assertions.Expect(page.GetByText("--hvo-series-1", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("--hvo-series-8", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("--hvo-accent-success", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("--shell-card-background", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("--shell-chart-grid-color", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-palette-swatch").First).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    private static async Task AssertChartRenderedAsync(IPage page, string chartId)
    {
        var canvas = page.Locator($"#{chartId}");
        await Assertions.Expect(canvas).ToBeVisibleAsync();
        await page.WaitForFunctionAsync($"() => Boolean(window.hvoChart?._instances?.['{chartId}'])");
        var chartWidth = await canvas.EvaluateAsync<int>("canvas => canvas.clientWidth");
        chartWidth.Should().BeGreaterThan(0);
    }

    private static async Task AssertNoBlazorErrorAsync(IPage page) =>
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();

    private static async Task AssertThemedSurfaceAsync(ILocator locator, string label)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync();
        var styleText = await locator.EvaluateAsync<string>(
            "el => { const s = window.getComputedStyle(el); return [s.getPropertyValue('background-color'), s.getPropertyValue('border-color'), s.getPropertyValue('border-radius')].join('|'); }");
        var parts = styleText.Split('|');
        var backgroundColor = parts.ElementAtOrDefault(0) ?? string.Empty;
        var borderColor = parts.ElementAtOrDefault(1) ?? string.Empty;
        var borderRadius = parts.ElementAtOrDefault(2) ?? string.Empty;
        var hasBackground = !string.Equals(backgroundColor, "rgba(0, 0, 0, 0)", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(backgroundColor, "transparent", StringComparison.OrdinalIgnoreCase);
        var hasBorder = !string.Equals(borderColor, "rgba(0, 0, 0, 0)", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(borderColor, "transparent", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(hasBackground || hasBorder, $"{label} should have themed background or border styling.");
        Assert.IsTrue(ParsePixels(borderRadius) >= 8, $"{label} should have themed rounded styling.");
    }

    private static double ParsePixels(string value)
    {
        var firstValue = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0";
        return double.TryParse(firstValue.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), out var pixels) ? pixels : 0;
    }
}

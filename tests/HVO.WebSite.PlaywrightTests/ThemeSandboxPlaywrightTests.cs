using FluentAssertions;
using HVO.WebSite.PlaywrightTests.Infrastructure;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
[TestCategory("Browser")]
public sealed class ThemeSandboxPlaywrightTests
{
    public TestContext TestContext { get; set; } = null!;
    private BrowserApplication<HVO.ThemeSandbox.Components.App> application = null!;
    private BrowserSession session = null!;
    private string BaseUrl => application.Address.ToString().TrimEnd('/');

    [TestInitialize]
    public async Task Open()
    {
        application = new("HVO.ThemeSandbox");
        try { session = await BrowserSession.OpenAsync(TestContext); }
        catch
        {
            await application.DisposeAsync();
            application = null!;
            throw;
        }
    }

    [TestCleanup]
    public async Task Close()
    {
        try
        {
            if (session is not null)
            {
                try { session.AssertNoUnexpectedErrors(); }
                finally { await session.DisposeAsync(); }
            }
        }
        finally { if (application is not null) await application.DisposeAsync(); }
    }

    private async Task<IPage> OpenPageAsync(string path = "/")
    {
        await session.Page.GotoAsync(new Uri(application.Address, path).ToString());
        return session.Page;
    }

    [TestMethod]
    public async Task ThemeSandbox_GatewayLayout_RendersBrandText()
    {
        var page = await OpenPageAsync();

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("THEME SANDBOX", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("THEME SANDBOX");
    }

    [TestMethod]
    public async Task ThemeSandbox_GatewayLayout_HasFooterSlots()
    {
        var page = await OpenPageAsync();

        var footer = page.Locator(".shell-footer-layout");
        await Assertions.Expect(footer).ToContainTextAsync("Theme Sandbox");
        await Assertions.Expect(footer).ToContainTextAsync("Phase 0.5 validation");
        await Assertions.Expect(footer).ToContainTextAsync("MudBlazor shell");
    }

    [TestMethod]
    public async Task ThemeSandbox_GatewayLayout_RendersDarkThemeInitially()
    {
        var page = await OpenPageAsync();

        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ThemeSandbox_FormatPage_ShowsMetricByDefault()
    {
        var page = await OpenPageAsync();

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

        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle unit system" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Imperial Units")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("72.5 °F")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle unit system" }).ClickAsync();
        await Assertions.Expect(page.GetByText("22.5 °C")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ThemeSandbox_HvoChart_RendersCanvas()
    {
        var page = await OpenPageAsync();

        await AssertChartRenderedAsync(page, "sandbox-chart-dense");
    }

    [TestMethod]
    public async Task ThemeSandbox_NavPills_HighlightActivePage()
    {
        var page = await OpenPageAsync("/dashboard");

        var gatewayPill = page.Locator("a.shell-nav-link-current");
        await Assertions.Expect(gatewayPill).ToHaveTextAsync("Dashboard");
        await page.GetByRole(AriaRole.Link, new() { Name = "Palette", Exact = true }).ClickAsync();
        await Assertions.Expect(gatewayPill).ToHaveTextAsync("Palette");
    }

    [TestMethod]
    public async Task ThemeSandbox_AdminLayout_HasSidebar()
    {
        var page = await OpenPageAsync("/admin-layout");

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Admin Panel" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "UI Controls" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ThemeSandbox_PublicLayout_RendersWithBrand()
    {
        var page = await OpenPageAsync("/public-layout");

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("THEME SANDBOX");
    }

    [TestMethod]
    public async Task ThemeSandbox_HvoCss_SharedClassesRender()
    {
        var page = await OpenPageAsync();

        await Assertions.Expect(page.Locator(".hvo-card").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-card-primary").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-card-note").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-metric").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-eyebrow").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-mono").First).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ThemeSandbox_NullValues_DisplayDashDash()
    {
        var page = await OpenPageAsync();

        var nullEntries = page.GetByText("--");
        var count = await nullEntries.CountAsync();
        Assert.IsTrue(count >= 3, $"Expected at least 3 null entries (--), found {count}");
    }

    [TestMethod]
    public async Task ThemeSandbox_Showcase_RendersCardPatterns()
    {
        var page = await OpenPageAsync("/theme-showcase");

        await Assertions.Expect(page.Locator(".hvo-ring-gauge").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-cell-grid")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-chip-row")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ThemeSandbox_HvoChart_RendersDenseAndSparseCharts()
    {
        var page = await OpenPageAsync();

        await AssertChartRenderedAsync(page, "sandbox-chart-dense");
        await AssertChartRenderedAsync(page, "sandbox-chart-sparse");
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task ThemeSandbox_ShouldKeepCircuitAliveAcrossAllReferenceRoutes()
    {
        var page = await OpenPageAsync();

        foreach (var route in new[] { "/", "/dashboard", "/controls", "/instruments", "/css-reference", "/theme-showcase", "/palette", "/states", "/responsive", "/admin-layout", "/public-layout" })
        {
            await page.GotoAsync($"{BaseUrl}{route}");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await AssertNoBlazorErrorAsync(page);
            await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        }
    }

    [TestMethod]
    public async Task ThemeSandbox_ShouldLoadOnlyLocalChartAndThemeResources()
    {
        var requestedUrls = new List<string>();
        var page = await OpenPageAsync();
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
    public async Task ThemeSandbox_CssReference_ShouldValidateControlsAndCardsStyling()
    {
        var page = await OpenPageAsync("/css-reference");

        await AssertThemedSurfaceAsync(page.Locator(".hvo-card-shell").First, "CSS reference card shell");
        await AssertThemedSurfaceAsync(page.Locator(".hvo-card").First, "CSS reference card");
        await AssertThemedSurfaceAsync(page.Locator(".hvo-control").First, "CSS reference form control");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "hvo-button-primary" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-chip-success").Filter(new() { HasText = "hvo-chip-success" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task ThemeSandbox_Palette_ShouldRenderCanonicalTokens()
    {
        var page = await OpenPageAsync("/palette");

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
        await BrowserBehaviorAssertions.ChartInitializedAsync(page, chartId);
        var chartWidth = await canvas.EvaluateAsync<int>("canvas => canvas.clientWidth");
        chartWidth.Should().BeGreaterThan(0);
    }

    private async Task AssertNoBlazorErrorAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        session.AssertNoUnexpectedErrors();
    }

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

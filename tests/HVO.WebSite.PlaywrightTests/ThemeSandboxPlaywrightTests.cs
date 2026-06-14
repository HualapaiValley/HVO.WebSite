using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class ThemeSandboxPlaywrightTests
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("HVO_THEMESANDBOX_BASE_URL") ?? "http://localhost:5199";

    private static async Task<IPage> OpenPageAsync(IPlaywright playwright, string path = "/")
    {
        var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{path}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return page;
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_GatewayLayout_RendersBrandText()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright);

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("THEME SANDBOX", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("THEME SANDBOX");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_GatewayLayout_HasFooterSlots()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright);

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
        var page = await OpenPageAsync(playwright);

        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" })).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_FormatPage_ShowsMetricByDefault()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright);

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
        var page = await OpenPageAsync(playwright);

        var canvas = page.Locator("#sandbox-chart");
        await Assertions.Expect(canvas).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_NavPills_HighlightActivePage()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright);

        var gatewayPill = page.Locator(".shell-nav-link-current");
        await Assertions.Expect(gatewayPill).ToContainTextAsync("Gateway");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_AdminLayout_HasSidebar()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright, "/admin-layout");

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Gateway Demo" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Public Layout" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Dashboard" })).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_PublicLayout_RendersWithBrand()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright, "/public-layout");

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("THEME SANDBOX");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_HvoCss_SharedClassesRender()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright);

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
        var page = await OpenPageAsync(playwright);

        var nullEntries = page.GetByText("--");
        var count = await nullEntries.CountAsync();
        Assert.IsTrue(count >= 3, $"Expected at least 3 null entries (--), found {count}");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task ThemeSandbox_Showcase_RendersCardPatterns()
    {
        using var playwright = await Playwright.CreateAsync();
        var page = await OpenPageAsync(playwright, "/theme-showcase");

        await Assertions.Expect(page.Locator(".hvo-ring-gauge").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-cell-grid")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-chip-row")).ToBeVisibleAsync();
    }
}

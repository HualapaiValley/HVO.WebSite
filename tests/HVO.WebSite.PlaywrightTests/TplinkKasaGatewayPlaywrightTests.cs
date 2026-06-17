using FluentAssertions;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class TplinkKasaGatewayPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task TplinkKasaGateway_ShouldRenderLayoutShell()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("TP-LINK/KASA LOCAL GATEWAY");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Overview" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Configuration" })).ToBeVisibleAsync();
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task TplinkKasaGateway_ShouldNavigateSettingsAndKeepCircuitAlive()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Configured Devices" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Configuration" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/settings/?$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Device Settings" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Scan Network")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Overview" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Configured Devices" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task TplinkKasaGateway_ShouldLoadOnlyLocalOfflineResources()
    {
        var requestedUrls = new List<string>();
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;
        page.Request += (_, request) => requestedUrls.Add(request.Url);

        await page.GotoAsync(BuildUrl("/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        requestedUrls.Should().NotContain(url =>
            url.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase)
            || url.Contains("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("unpkg.com", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-components.css", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/MudBlazor/MudBlazor.min.css", StringComparison.OrdinalIgnoreCase));
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task TplinkKasaGateway_ShouldRenderThemedDashboardAndSettingsControls()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));
        await AssertThemedSurfaceAsync(page.Locator(".kasa-panel, .kasa-device-card").First, "Dashboard panel or device card");
        await Assertions.Expect(page.Locator(".kasa-tabs")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GotoAsync(BuildUrl("/settings"));
        await AssertThemedSurfaceAsync(page.Locator(".kasa-panel").First, "Settings panel");
        await Assertions.Expect(page.GetByLabel("IP or host")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Scan" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task TplinkKasaGateway_ShouldOpenGatewayInfoDialogAndCloseIt()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Open Kasa gateway information" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog).ToContainTextAsync("TP-Link/Kasa local gateway");
        await Assertions.Expect(dialog).ToContainTextAsync("Gateway ID");
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Close gateway information" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task TplinkKasaGateway_ShouldHaveNoLegacyClasses()
    {
        var session = await CreatePageAsync();
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));
        await AssertNoLegacyClassesAsync(page, "Dashboard");
        await AssertNoBlazorErrorAsync(page);

        await page.GotoAsync(BuildUrl("/settings"));
        await AssertNoLegacyClassesAsync(page, "Settings");
        await AssertNoBlazorErrorAsync(page);
    }

    private static async Task<(IPlaywright Playwright, IBrowser Browser, IPage Page)> CreatePageAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_TPLINKKASA_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_TPLINKKASA_BASE_URL to run the TP-Link/Kasa gateway UI Playwright tests.");
        }

        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync(new BrowserNewPageOptions
            {
                ViewportSize = new ViewportSize { Width = 1600, Height = 1000 }
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
        var baseUrl = Environment.GetEnvironmentVariable("HVO_TPLINKKASA_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Inconclusive("Set HVO_TPLINKKASA_BASE_URL to run the TP-Link/Kasa gateway UI Playwright tests.");
        }

        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), route.TrimStart('/')).ToString();
    }

    private static async Task AssertNoBlazorErrorAsync(IPage page) =>
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();

    private static async Task AssertNoLegacyClassesAsync(IPage page, string label)
    {
        var legacyCount = await page.Locator("[class*='proto-'], .action-btn, .card-shell, .archive-table").CountAsync();
        Assert.AreEqual(0, legacyCount, $"{label} should use shared shell/hvo theme classes instead of legacy prototype classes.");
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

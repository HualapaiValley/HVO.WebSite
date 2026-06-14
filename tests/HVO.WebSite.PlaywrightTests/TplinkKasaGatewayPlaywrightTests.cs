using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class TplinkKasaGatewayPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task TplinkKasaGateway_ShouldRenderLayoutShell()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_TPLINKKASA_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            Assert.Inconclusive("Set HVO_TPLINKKASA_BASE_URL to run the TP-Link/Kasa gateway UI Playwright test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(baseUrl);

        await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".shell-brand-subtitle")).ToContainTextAsync("TP-LINK/KASA LOCAL GATEWAY");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Overview" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Configuration" })).ToBeVisibleAsync();
    }
}

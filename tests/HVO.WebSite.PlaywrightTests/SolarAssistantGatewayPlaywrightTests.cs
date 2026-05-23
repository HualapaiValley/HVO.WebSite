using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class SolarAssistantGatewayPlaywrightTests
{
    [TestMethod]
    public async Task SolarAssistantGateway_ShouldRenderMonitorShell()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_SOLARASSISTANT_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            Assert.Inconclusive("Set HVO_SOLARASSISTANT_BASE_URL to run the SolarAssistant gateway UI Playwright test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(baseUrl);

        await Assertions.Expect(page.GetByText("SOLARASSISTANT GATEWAY", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "REST JSON" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "MQTT JSON" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "SolarAssistant Gateway" })).ToBeAttachedAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Current Snapshot" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Home Assistant Discovery" })).ToBeVisibleAsync();
    }
}

using FluentAssertions;
using Microsoft.Playwright;

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
}

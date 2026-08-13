using Microsoft.Playwright;
using System.Text.Json;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class HomeAssistantEnergyDashboardPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task EnergyDashboard_ShouldRenderThreePvInputsAndHistoryAtDesktopAndMobileWidths()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_URL");
        var accessToken = Environment.GetEnvironmentVariable("HOME_ASSISTANT_TOKEN");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
            Assert.Inconclusive("Set HVO_HOME_ASSISTANT_URL and HOME_ASSISTANT_TOKEN to run the Home Assistant Energy dashboard test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 1600, Height = 1000 },
                     new ViewportSize { Width = 390, Height = 844 }
                 })
        {
            await using var context = await browser.NewContextAsync(new() { ViewportSize = viewport });
            var hassUrlJson = JsonSerializer.Serialize(baseUrl.TrimEnd('/'));
            var accessTokenJson = JsonSerializer.Serialize(accessToken);
            await context.AddInitScriptAsync($$"""
                localStorage.setItem('hassTokens', JSON.stringify({
                    hassUrl: {{hassUrlJson}},
                    clientId: `${location.origin}/`,
                    access_token: {{accessTokenJson}},
                    refresh_token: {{accessTokenJson}},
                    expires_in: 86400,
                    expires: Date.now() + 86400000
                }));
                """);

            var page = await context.NewPageAsync();
            await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-energy/energy");
            await Assertions.Expect(page.GetByText("Three PV Inputs", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("6500EX MPPT 1", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("6500EX MPPT 2", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("External MPPT", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("AC Consumption", new() { Exact = true })).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, viewport.Width, "energy");

            await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-energy/history");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "PV Inputs - 24 Hours", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Daily Energy", Exact = true })).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, viewport.Width, "history");
        }
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page, int viewportWidth, string view)
    {
        var metrics = await page.Locator("body").EvaluateAsync<BodyMetrics>(
            "body => ({ width: body.clientWidth, scrollWidth: body.scrollWidth })");
        Assert.IsTrue(metrics.ScrollWidth <= metrics.Width + 2,
            $"{view} should not create horizontal overflow at {viewportWidth}px.");
    }

    private sealed class BodyMetrics
    {
        public int Width { get; set; }
        public int ScrollWidth { get; set; }
    }
}

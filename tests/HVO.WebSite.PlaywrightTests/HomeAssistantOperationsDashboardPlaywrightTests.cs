using System.Text.Json;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class HomeAssistantOperationsDashboardPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task OperationsDashboard_ShouldRenderCriticalViewsAtDesktopAndMobileWidths()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_URL");
        var accessToken = Environment.GetEnvironmentVariable("HOME_ASSISTANT_TOKEN");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
            Assert.Inconclusive("Set HVO_HOME_ASSISTANT_URL and HOME_ASSISTANT_TOKEN to run the Home Assistant Operations dashboard test.");

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
            await AssertViewAsync(page, $"{baseUrl.TrimEnd('/')}/hvo-operations/environment", "Observatory Sensors", viewport.Width);
            await AssertViewAsync(page, $"{baseUrl.TrimEnd('/')}/hvo-operations/weather", "Wind And Rain", viewport.Width);
            await AssertViewAsync(page, $"{baseUrl.TrimEnd('/')}/hvo-operations/batteries", "JK Bank State Of Charge", viewport.Width);
            await AssertViewAsync(page, $"{baseUrl.TrimEnd('/')}/hvo-operations/gateways", "Outbox Pending", viewport.Width);
        }
    }

    private static async Task AssertViewAsync(IPage page, string url, string heading, int viewportWidth)
    {
        await page.GotoAsync(url);
        await Assertions.Expect(page.GetByText(heading, new() { Exact = true })).ToBeVisibleAsync();
        var metrics = await page.Locator("body").EvaluateAsync<BodyMetrics>(
            "body => ({ width: body.clientWidth, scrollWidth: body.scrollWidth })");
        Assert.IsTrue(metrics.ScrollWidth <= metrics.Width + 2,
            $"{heading} should not create horizontal overflow at {viewportWidth}px.");
    }

    private sealed class BodyMetrics
    {
        public int Width { get; set; }
        public int ScrollWidth { get; set; }
    }
}

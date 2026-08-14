using System.Text.Json;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class HomeAssistantWeatherDashboardPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task WeatherDashboard_ShouldRenderInstrumentsAndTrendsAtDesktopAndMobileWidths()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_URL");
        var accessToken = Environment.GetEnvironmentVariable("HOME_ASSISTANT_TOKEN");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
            Assert.Inconclusive("Set HVO_HOME_ASSISTANT_URL and HOME_ASSISTANT_TOKEN to run the Home Assistant Weather dashboard test.");

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
            await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-weather/overview");
            await Assertions.Expect(page.GetByText("Current Conditions", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Davis Wind Instrument", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("wind-compass")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("External Cloud And Forecast", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Station Health", new() { Exact = true })).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, viewport.Width, "weather overview");
            await AssertWindCardContainersFitAsync(page, viewport.Width);

            await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-weather/trends");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Temperature And Dew Point - 24 Hours", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Rain Accumulation - 7 Days", Exact = true })).ToBeVisibleAsync();
            await AssertNoHorizontalOverflowAsync(page, viewport.Width, "weather trends");
        }
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page, int viewportWidth, string view)
    {
        var metrics = await page.Locator("body").EvaluateAsync<BodyMetrics>(
            "body => ({ width: body.clientWidth, scrollWidth: body.scrollWidth })");
        Assert.IsTrue(metrics.ScrollWidth <= metrics.Width + 2,
            $"{view} should not create horizontal overflow at {viewportWidth}px.");
        var overflowingCards = await page.Locator("ha-card").EvaluateAllAsync<string[]>(
            """
            cards => cards
                .filter(card => card.clientWidth > 0 && (card.scrollWidth > card.clientWidth + 2 || card.getBoundingClientRect().right > document.documentElement.clientWidth + 2))
                .map(card => card.tagName.toLowerCase() + (card.className ? '.' + String(card.className).replaceAll(' ', '.') : ''))
            """);
        Assert.IsEmpty(overflowingCards, $"{view} cards should fit at {viewportWidth}px.");
    }

    private static async Task AssertWindCardContainersFitAsync(IPage page, int viewportWidth)
    {
        var metrics = await page.Locator("hvo-weather-wind-card").EvaluateAsync<BodyMetrics[]>(
            """
            element => [element, element.shadowRoot.querySelector('ha-card'), element.shadowRoot.querySelector('.content')]
                .map(node => ({ width: node.clientWidth, scrollWidth: node.scrollWidth }))
            """);
        Assert.IsTrue(metrics.All(metric => metric.ScrollWidth <= metric.Width + 2),
            $"Wind card host, shadow card, and content should fit at {viewportWidth}px.");
    }

    private sealed class BodyMetrics
    {
        public int Width { get; set; }
        public int ScrollWidth { get; set; }
    }
}

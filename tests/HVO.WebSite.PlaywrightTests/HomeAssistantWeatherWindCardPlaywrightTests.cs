using FluentAssertions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class HomeAssistantWeatherWindCardPlaywrightTests
{
    private static string CardPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "hvo-weather-wind-card.js");

    [TestMethod]
    [DataRow(0, "N")]
    [DataRow(45, "NE")]
    [DataRow(90, "E")]
    [DataRow(225, "SW")]
    [DataRow(359, "N")]
    public async Task WindCard_RendersNumericAndCardinalDirection(int degrees, string cardinal)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await RenderCardAsync(page, degrees, DateTimeOffset.UtcNow, "live");

        await Assertions.Expect(page.GetByTestId("wind-cardinal")).ToHaveTextAsync(cardinal);
        await Assertions.Expect(page.GetByTestId("wind-compass"))
            .ToHaveAttributeAsync("aria-label", $"Wind direction {degrees} degrees {cardinal}");
        var direction = await page.GetByTestId("wind-needle").GetAttributeAsync("style");
        direction.Should().Contain($"--direction: {degrees}deg");
    }

    [TestMethod]
    [DataRow("live", "Current")]
    [DataRow("stale", "Stale")]
    [DataRow("error", "Error")]
    [DataRow("waiting", "Waiting")]
    [DataRow("unknown", "Waiting")]
    [DataRow("unavailable", "Unavailable")]
    public async Task WindCard_UsesAuthoritativeCollectorFreshness(string freshness, string expected)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await RenderCardAsync(page, 180, DateTimeOffset.UtcNow, freshness);

        await Assertions.Expect(page.GetByTestId("wind-status")).ToHaveTextAsync(expected);
    }

    [TestMethod]
    public async Task WindCard_ObservationAgeIsSupportingDetailAndDoesNotOverrideLiveFreshness()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await RenderCardAsync(page, 180, DateTimeOffset.UtcNow.AddMinutes(-10), "live");

        await Assertions.Expect(page.GetByTestId("wind-status")).ToHaveTextAsync("Current");
        await Assertions.Expect(page.GetByTestId("wind-observation-age")).ToContainTextAsync("10m ago");
    }

    [TestMethod]
    public async Task WindCard_MissingReadingsDegradeWithoutOverridingCollectorFreshness()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await RenderCardAsync(page, null, DateTimeOffset.UtcNow, "live");

        await Assertions.Expect(page.GetByTestId("wind-status")).ToHaveTextAsync("Current");
        await Assertions.Expect(page.GetByTestId("wind-cardinal")).ToHaveTextAsync("--");
        await Assertions.Expect(page.GetByTestId("wind-observation-age")).ToContainTextAsync("Wind readings unavailable");
    }

    [TestMethod]
    public async Task WindCard_FitsHostShadowCardAndContentAtPhoneWidth()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 320, Height = 700 } });
        await RenderCardAsync(page, 180, DateTimeOffset.UtcNow, "stale");

        await Assertions.Expect(page.GetByTestId("wind-status")).ToHaveTextAsync("Stale");
        var metrics = await page.Locator("hvo-weather-wind-card").EvaluateAsync<ElementMetrics[]>(
            """
            element => [element, element.shadowRoot.querySelector('ha-card'), element.shadowRoot.querySelector('.content')]
                .map(node => ({ width: node.clientWidth, scrollWidth: node.scrollWidth }))
            """);
        metrics.Should().OnlyContain(metric => metric.ScrollWidth <= metric.Width + 2,
            "the host, shadow card, and card content should fit a 320px viewport");
    }

    private static async Task RenderCardAsync(
        IPage page,
        int? direction,
        DateTimeOffset updatedAt,
        string freshness)
    {
        await page.SetContentAsync("<hvo-weather-wind-card></hvo-weather-wind-card>");
        await page.AddScriptTagAsync(new() { Path = CardPath, Type = "module" });
        await page.Locator("hvo-weather-wind-card").EvaluateAsync(
            """
            (element, input) => {
                element.setConfig({
                    title: 'Davis Wind Instrument',
                    freshness_entity: 'sensor.freshness',
                    direction_entity: 'sensor.direction',
                    speed_entity: 'sensor.speed',
                    average_2_entity: 'sensor.average_2',
                    average_10_entity: 'sensor.average_10',
                    gust_entity: 'sensor.gust',
                    gust_direction_entity: 'sensor.gust_direction'
                });
                const state = (value) => ({ state: String(value), last_updated: input.updatedAt });
                const states = {
                    'sensor.freshness': state(input.freshness),
                    'sensor.speed': state(12),
                    'sensor.average_2': state(10.5),
                    'sensor.average_10': state(8.5),
                    'sensor.gust': state(20),
                    'sensor.gust_direction': state(200)
                };
                if (input.direction !== null) states['sensor.direction'] = state(input.direction);
                element.hass = { states };
            }
            """,
            new { direction, updatedAt = updatedAt.ToString("O"), freshness });
    }

    private sealed class ElementMetrics
    {
        public int Width { get; set; }
        public int ScrollWidth { get; set; }
    }
}

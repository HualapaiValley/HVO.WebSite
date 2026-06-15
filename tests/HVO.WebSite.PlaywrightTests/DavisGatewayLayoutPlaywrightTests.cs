using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class DavisGatewayLayoutPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task DavisGateway_ShouldRenderStyledLayouts()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_DAVIS_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            Assert.Inconclusive("Set HVO_DAVIS_BASE_URL to run the Davis gateway UI Playwright test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1800, Height = 1200 }
        });

        foreach (var route in new[] { "/", "/alarms", "/archive", "/console-settings" })
        {
            await page.GotoAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), route.TrimStart('/')).ToString());

            await Assertions.Expect(page.GetByText("Hualapai Valley Observatory", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".layout-canvas.shell-page")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".shell-page-stack").First).ToBeVisibleAsync();

            var layoutMetrics = await page.Locator(".layout-canvas.shell-page").EvaluateAsync<LayoutMetrics>(
                "el => ({ width: el.clientWidth, scrollWidth: el.scrollWidth, height: el.clientHeight, scrollHeight: el.scrollHeight, overflowY: getComputedStyle(el).overflowY })");

            Assert.IsTrue(layoutMetrics.Width > 1200, $"{route} should use the available desktop content width.");
            Assert.IsTrue(layoutMetrics.OverflowY is "auto" or "scroll", $"{route} should keep scrolling on the shared layout canvas.");
            Assert.IsTrue(layoutMetrics.ScrollWidth <= layoutMetrics.Width + 8, $"{route} should not create page-level horizontal overflow.");
        }

        await page.GotoAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "alarms").ToString());
        await Assertions.Expect(page.Locator(".proto-alarm-grid")).ToBeVisibleAsync();
        var alarmGridColumns = await page.Locator(".proto-alarm-grid").EvaluateAsync<string>("el => getComputedStyle(el).gridTemplateColumns");
        Assert.AreNotEqual("none", alarmGridColumns);
        Assert.IsTrue(alarmGridColumns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2, "Alerts should render as a two-column desktop grid.");
        await AssertStyledCardAsync(page.Locator(".alarm-definitions-page .proto-card").First, "Alerts rule editor card");
        await AssertStyledControlAsync(page.Locator(".alarm-definitions-page .proto-select").First, "Alerts select");
        await AssertStyledControlAsync(page.Locator(".alarm-definitions-page .proto-button").First, "Alerts button");

        await page.GotoAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "archive").ToString());
        await AssertStyledCardAsync(page.Locator(".archive-page .card-shell").First, "Archive status card");
        await AssertStyledControlAsync(page.Locator(".archive-page select").First, "Archive select");
        await AssertStyledControlAsync(page.Locator(".archive-page .action-btn").First, "Archive button");

        await page.GotoAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "console-settings").ToString());
        await AssertStyledCardAsync(page.Locator(".console-settings-page .proto-card").First, "Configuration card");
        await AssertStyledControlAsync(page.Locator(".console-settings-page .proto-input").First, "Configuration input");
        await AssertStyledControlAsync(page.Locator(".console-settings-page .proto-select").First, "Configuration select");
    }

    private static async Task AssertStyledCardAsync(ILocator locator, string label)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync();
        var style = await locator.EvaluateAsync<ElementStyle>(
            "el => ({ backgroundColor: getComputedStyle(el).backgroundColor, borderRadius: getComputedStyle(el).borderRadius, display: getComputedStyle(el).display })");

        Assert.AreNotEqual("rgba(0, 0, 0, 0)", style.BackgroundColor, $"{label} should have a themed background.");
        Assert.IsTrue(ParsePixels(style.BorderRadius) >= 12, $"{label} should have a card radius.");
    }

    private static async Task AssertStyledControlAsync(ILocator locator, string label)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync();
        var style = await locator.EvaluateAsync<ElementStyle>(
            "el => ({ backgroundColor: getComputedStyle(el).backgroundColor, borderRadius: getComputedStyle(el).borderRadius, display: getComputedStyle(el).display })");

        Assert.AreNotEqual("rgb(255, 255, 255)", style.BackgroundColor, $"{label} should not use browser default white styling.");
        Assert.IsTrue(ParsePixels(style.BorderRadius) >= 10, $"{label} should have themed rounded styling.");
    }

    private static double ParsePixels(string value)
    {
        var firstValue = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0";
        return double.TryParse(firstValue.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), out var pixels) ? pixels : 0;
    }

    private sealed class LayoutMetrics
    {
        public int Width { get; set; }

        public int ScrollWidth { get; set; }

        public int Height { get; set; }

        public int ScrollHeight { get; set; }

        public string OverflowY { get; set; } = string.Empty;
    }

    private sealed class ElementStyle
    {
        public string BackgroundColor { get; set; } = string.Empty;

        public string BorderRadius { get; set; } = string.Empty;

        public string Display { get; set; } = string.Empty;
    }
}

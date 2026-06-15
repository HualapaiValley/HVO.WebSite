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
        await Assertions.Expect(page.Locator(".alarm-grid")).ToBeVisibleAsync();
        var alarmGridColumns = await page.Locator(".alarm-grid").EvaluateAsync<string>("el => getComputedStyle(el).gridTemplateColumns");
        Assert.AreNotEqual("none", alarmGridColumns);
        Assert.IsTrue(alarmGridColumns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2, "Alerts should render as a two-column desktop grid.");
        await AssertStyledCardAsync(page.Locator(".alarm-definitions-page .hvo-card-shell").First, "Alerts rule editor card");
        await AssertStyledControlAsync(page.Locator(".alarm-definitions-page .hvo-control").First, "Alerts control");
        await AssertStyledControlAsync(page.Locator(".alarm-definitions-page .hvo-button").First, "Alerts button");
        await AssertNoLegacyLiveClassesAsync(page, "Alerts");

        await page.GotoAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "archive").ToString());
        await AssertStyledCardAsync(page.Locator(".archive-page .hvo-card-shell").First, "Archive status card");
        await AssertStyledControlAsync(page.Locator(".archive-page select").First, "Archive select");
        await AssertStyledControlAsync(page.Locator(".archive-page input[type='datetime-local']").First, "Archive datetime input");
        await AssertStyledControlAsync(page.Locator(".archive-page .hvo-button").First, "Archive button");
        await AssertNoLegacyLiveClassesAsync(page, "Archive");

        await page.GotoAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "console-settings").ToString());
        await AssertStyledCardAsync(page.Locator(".console-settings-page .hvo-card-shell").First, "Configuration card");
        await AssertStyledControlAsync(page.Locator(".console-settings-page .hvo-control").First, "Configuration input");
        await AssertStyledControlAsync(page.Locator(".console-settings-page select.hvo-control").First, "Configuration select");
        await AssertStyledControlAsync(page.Locator(".console-settings-page input[type='datetime-local']").First, "Configuration datetime input");
        await AssertNoLegacyLiveClassesAsync(page, "Configuration");
    }

    private static async Task AssertStyledCardAsync(ILocator locator, string label)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync();
        var style = await GetStyleAsync(locator);

        Assert.AreNotEqual("rgba(0, 0, 0, 0)", style.BackgroundColor, $"{label} should have a themed background. Style: {style}");
        Assert.IsTrue(ParsePixels(style.BorderRadius) >= 12, $"{label} should have a card radius. Style: {style}");
    }

    private static async Task AssertStyledControlAsync(ILocator locator, string label)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync();
        var style = await GetStyleAsync(locator);

        Assert.AreNotEqual("rgb(255, 255, 255)", style.BackgroundColor, $"{label} should not use browser default white styling. Style: {style}");
        Assert.IsTrue(ParsePixels(style.BorderRadius) >= 10, $"{label} should have themed rounded styling. Style: {style}");
    }

    private static async Task AssertNoLegacyLiveClassesAsync(IPage page, string label)
    {
        var legacyCount = await page.Locator("[class*='proto-'], .action-btn, .card-shell, .archive-table").CountAsync();
        Assert.AreEqual(0, legacyCount, $"{label} should use shared hvo-* theme classes instead of legacy page/prototype classes.");
    }

    private static async Task<ElementStyle> GetStyleAsync(ILocator locator)
    {
        var styleText = await locator.EvaluateAsync<string>(
            "el => { const s = window.getComputedStyle(el); return [s.getPropertyValue('background-color'), s.getPropertyValue('border-radius'), s.getPropertyValue('display'), el.getAttribute('class') || el.tagName].join('|'); }");
        var parts = styleText.Split('|');

        return new ElementStyle
        {
            BackgroundColor = parts.ElementAtOrDefault(0) ?? string.Empty,
            BorderRadius = parts.ElementAtOrDefault(1) ?? string.Empty,
            Display = parts.ElementAtOrDefault(2) ?? string.Empty,
            ClassName = parts.ElementAtOrDefault(3) ?? string.Empty
        };
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

        public string ClassName { get; set; } = string.Empty;

        public override string ToString() => $"background={BackgroundColor}, radius={BorderRadius}, display={Display}, class={ClassName}";
    }
}

using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

public static class PlaywrightGatewayAssertions
{
    public static async Task AssertNoBlazorErrorAsync(IPage page) =>
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();

    public static Task AssertNoCdnResourcesAsync(IReadOnlyCollection<string> requestedUrls)
    {
        var cdnUrl = requestedUrls.FirstOrDefault(url =>
            url.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase)
            || url.Contains("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("fonts.gstatic.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("unpkg.com", StringComparison.OrdinalIgnoreCase));

        Assert.IsNull(cdnUrl, $"Gateway pages must not request CDN resources. Unexpected URL: {cdnUrl}");
        return Task.CompletedTask;
    }

    public static Task AssertLocalThemeResourcesLoadedAsync(IReadOnlyCollection<string> requestedUrls)
    {
        Assert.IsTrue(
            requestedUrls.Any(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-dark.css", StringComparison.OrdinalIgnoreCase)),
            "Gateway pages should load the shared hvo-dark.css stylesheet locally.");
        Assert.IsTrue(
            requestedUrls.Any(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css", StringComparison.OrdinalIgnoreCase)),
            "Gateway pages should load the shared shell stylesheet locally.");
        Assert.IsTrue(
            requestedUrls.Any(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-components.css", StringComparison.OrdinalIgnoreCase)),
            "Gateway pages should load the shared components stylesheet locally.");

        return Task.CompletedTask;
    }

    public static Task AssertLocalChartResourcesLoadedAsync(IReadOnlyCollection<string> requestedUrls)
    {
        Assert.IsTrue(
            requestedUrls.Any(url => url.Contains("_content/HVO.WebSite.Themes/js/chart.min.js", StringComparison.OrdinalIgnoreCase)),
            "Gateway pages should load Chart.js from the shared Themes RCL.");
        Assert.IsTrue(
            requestedUrls.Any(url => url.Contains("_content/HVO.WebSite.Themes/js/hvo-chart.js", StringComparison.OrdinalIgnoreCase)),
            "Gateway pages should load the shared HvoChart interop script locally.");

        return Task.CompletedTask;
    }

    public static async Task AssertNoLegacyClassesAsync(IPage page, string label)
    {
        var legacyCount = await page.Locator("[class*='proto-'], .action-btn, .card-shell, .archive-table").CountAsync();
        Assert.AreEqual(0, legacyCount, $"{label} should use shared hvo-* theme classes instead of legacy page/prototype classes.");
    }

    public static async Task AssertChartRenderedAsync(IPage page, string chartId)
    {
        var chart = page.Locator($"#{chartId}");
        await Assertions.Expect(chart).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("id => Boolean(window.hvoChart?._instances?.[id])", chartId);

        var chartSize = await chart.EvaluateAsync<ChartSize>("canvas => ({ width: canvas.clientWidth, height: canvas.clientHeight })");
        Assert.IsTrue(chartSize.Width > 0, $"Chart {chartId} should render with a positive width.");
        Assert.IsTrue(chartSize.Height > 0, $"Chart {chartId} should render with a positive height.");
    }

    private sealed class ChartSize
    {
        public int Width { get; set; }

        public int Height { get; set; }
    }
}

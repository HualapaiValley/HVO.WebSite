using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

/// <summary>Assertions against real browser behavior, also qualified by injected failures.</summary>
internal static class BrowserBehaviorAssertions
{
    public static async Task ChartInitializedAsync(IPage page, string chartId, float timeout = 8000)
    {
        await Assertions.Expect(page.Locator("#" + chartId)).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("""
            id => {
                const canvas = document.getElementById(id);
                const chart = window.Chart?.getChart(canvas);
                return canvas?.clientWidth > 0 && canvas.clientHeight > 0 &&
                    chart?.canvas === canvas && chart.data.datasets.length > 0;
            }
            """, chartId, new() { Timeout = timeout });
    }

    public static async Task SwitchToLightAsync(IPage page, float timeout = 8000)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.shell-theme-light') !== null", null,
            new() { Timeout = timeout });
    }
}

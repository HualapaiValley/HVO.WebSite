using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

/// <summary>Assertions against real browser behavior, also qualified by injected failures.</summary>
internal static class BrowserBehaviorAssertions
{
    private static readonly Regex NonTransparentColor = new(@"^(?!transparent$|rgba\(0, 0, 0, 0\)$).+$");

    public static async Task ThemedSurfaceAsync(ILocator locator, string label, string borderRadius, float timeout = 5000)
    {
        // Hydration replaces prerendered elements. Native locator assertions re-resolve
        // and retry; EvaluateAsync can read an already-detached handle and return empty CSS.
        await Assertions.Expect(locator, label).ToBeVisibleAsync(new() { Timeout = timeout });
        await Assertions.Expect(locator, label).ToHaveCSSAsync("border-radius", borderRadius, new() { Timeout = timeout });
        await Assertions.Expect(locator, label).ToHaveCSSAsync("background-color", NonTransparentColor, new() { Timeout = timeout });
        await Assertions.Expect(locator, label).ToHaveCSSAsync("border-color", NonTransparentColor, new() { Timeout = timeout });
    }

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

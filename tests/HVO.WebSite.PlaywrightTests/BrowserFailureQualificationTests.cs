using FluentAssertions;
using HVO.WebSite.PlaywrightTests.Infrastructure;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
[TestCategory("Browser")]
public sealed class BrowserFailureQualificationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MissingChartScriptFailsTheSameAssertionDespiteVisibleCanvas_ThenRecovers()
    {
        await using var application = new BrowserApplication<HVO.ThemeSandbox.Components.App>("HVO.ThemeSandbox");
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        var blocked = 0;
        await page.RouteAsync("**/chart.min.js", route =>
        {
            Interlocked.Increment(ref blocked);
            // A successful empty response models a missing script without incidental network-console errors.
            return route.FulfillAsync(new() { Status = 200, ContentType = "application/javascript", Body = "/* missing Chart.js */" });
        });
        await page.GotoAsync(application.Address.ToString());
        await Assertions.Expect(page.Locator("#sandbox-chart-dense")).ToBeVisibleAsync();
        Func<Task> verify = () => BrowserBehaviorAssertions.ChartInitializedAsync(page, "sandbox-chart-dense", 750);
        await verify.Should().ThrowAsync<TimeoutException>("a visible canvas cannot stand in for a real Chart.js instance");
        blocked.Should().BeGreaterThan(0);
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        await page.UnrouteAsync("**/chart.min.js");
        await page.ReloadAsync();
        await BrowserBehaviorAssertions.ChartInitializedAsync(page, "sandbox-chart-dense");
        // Expected missing-library errors are retained in the console artifact.
    }

    [TestMethod]
    public async Task BrokenThemeHandlerFailsTheSameInteractionAssertion_ThenRecovers()
    {
        await using var application = new BrowserApplication<HVO.ThemeSandbox.Components.App>("HVO.ThemeSandbox");
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await page.GotoAsync(application.Address.ToString());
        await BrowserBehaviorAssertions.ChartInitializedAsync(page, "sandbox-chart-dense");
        await page.EvaluateAsync("""
            () => {
                window.blockThemeClick = event => {
                    if (event.target.closest('button[aria-label="Switch to light theme"]')) {
                        event.stopImmediatePropagation();
                        event.preventDefault();
                    }
                };
                document.addEventListener('click', window.blockThemeClick, true);
            }
            """);
        Func<Task> verify = () => BrowserBehaviorAssertions.SwitchToLightAsync(page, 750);
        await verify.Should().ThrowAsync<TimeoutException>("a rendered button cannot stand in for its working handler");
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
        await page.EvaluateAsync("document.removeEventListener('click', window.blockThemeClick, true)");
        await BrowserBehaviorAssertions.SwitchToLightAsync(page);
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        browser.AssertNoUnexpectedErrors();
    }
}

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
    public async Task HydrationDetachesPrerenderedControl_LocatorStyleAssertionReadsReplacement()
    {
        await using var application = new BrowserApplication<HVO.ThemeSandbox.Components.App>("HVO.ThemeSandbox");
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        var allowHydration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/_blazor/negotiate?*", async route =>
        {
            await allowHydration.Task;
            await route.ContinueAsync();
        });
        try
        {
            await page.GotoAsync(new Uri(application.Address, "/css-reference").ToString());
            var control = page.Locator(".hvo-control").First;
            var prerenderedControl = await control.ElementHandleAsync()
                ?? throw new AssertFailedException("Expected a prerendered form control.");
            (await prerenderedControl.EvaluateAsync<bool>("el => el.isConnected")).Should().BeTrue();
            (await prerenderedControl.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius")).Should().Be("14px");

            allowHydration.SetResult();
            await page.WaitForFunctionAsync("el => !el.isConnected", prerenderedControl);
            // This is the empty-style failure of the old one-shot ElementHandle read.
            (await prerenderedControl.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius")).Should().BeEmpty();
            await BrowserBehaviorAssertions.ThemedSurfaceAsync(control, "Hydrated reference control", "14px");
            await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
            browser.AssertNoUnexpectedErrors();
        }
        finally { allowHydration.TrySetResult(); }
    }

    [TestMethod]
    [DataRow("border-radius", "0px")]
    [DataRow("background-color", "transparent")]
    [DataRow("border-color", "transparent")]
    public async Task BrokenSurfaceStyleFailsTheSameAssertion_ThenRecovers(string property, string value)
    {
        await using var application = new BrowserApplication<HVO.ThemeSandbox.Components.App>("HVO.ThemeSandbox");
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await page.GotoAsync(new Uri(application.Address, "/css-reference").ToString());
        var control = page.Locator(".hvo-control").First;
        await BrowserBehaviorAssertions.ThemedSurfaceAsync(control, "Reference control", "14px");
        var brokenStyle = await page.AddStyleTagAsync(new()
        {
            Content = $".css-ref-page .hvo-control {{ {property}: {value} !important; }}"
        });
        await Assertions.Expect(control).ToBeVisibleAsync();
        Func<Task> verify = () => BrowserBehaviorAssertions.ThemedSurfaceAsync(control, "Broken reference control", "14px", 750);
        await verify.Should().ThrowAsync<PlaywrightException>().WithMessage($"*{property}*");
        await brokenStyle.EvaluateAsync("el => el.remove()");
        await BrowserBehaviorAssertions.ThemedSurfaceAsync(control, "Recovered reference control", "14px");
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();
        browser.AssertNoUnexpectedErrors();
    }

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

using FluentAssertions;
using HVO.WebSite.PlaywrightTests.Infrastructure;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass, TestCategory("Browser"), DoNotParallelize]
public sealed class ThemeSandboxInstrumentStyleBrowserTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod, Timeout(30000)]
    [DataRow(false), DataRow(true)]
    public async Task InstrumentsApplyScopedLayoutInBothThemes(bool light)
    {
        await using var application = new BrowserApplication<HVO.ThemeSandbox.Components.App>("HVO.ThemeSandbox");
        await using var session = await BrowserSession.OpenAsync(TestContext);
        await session.Page.GotoAsync(new Uri(application.Address, "/instruments").ToString());
        await session.Page.WaitForFunctionAsync("() => Object.keys(window.hvoChart?._instances || {}).length === 7");
        if (light)
        {
            await session.Page.GetByRole(AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();
            await session.Page.WaitForSelectorAsync(".shell-theme-light");
        }

        (await session.Page.Locator(".id-compass-grid").EvaluateAsync<string>("element => getComputedStyle(element).display"))
            .Should().Be("grid", "the authored instrument layout must actually be applied");
        var widths = await session.Page.Locator(".id-compass-svg").EvaluateAllAsync<double[]>(
            "elements => elements.map(element => element.getBoundingClientRect().width)");
        widths.Should().HaveCount(6);
        widths.Should().OnlyContain(width => width > 0 && width <= 80.1,
            "instrument compasses must stay compact rather than expanding to the full page width");
        (await session.Page.Locator(".id-page").EvaluateAsync<string>(
            "element => getComputedStyle(element).getPropertyValue('--demo-compass-face').trim()"))
            .Should().NotBeNullOrEmpty("the computed compass token comes from the scoped stylesheet");
        (await session.Page.Locator("#blazor-error-ui").IsVisibleAsync()).Should().BeFalse();
        session.AssertNoUnexpectedErrors();
    }
}

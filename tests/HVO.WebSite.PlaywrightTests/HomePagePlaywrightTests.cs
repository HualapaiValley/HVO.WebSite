using Microsoft.Playwright.MSTest;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public class HomePagePlaywrightTests : PageTest
{
    [TestMethod]
    [Ignore("Requires Playwright browser install and a running site. Set HVO_WEBSITE_BASE_URL and run browser install before enabling.")]
    public async Task HomePage_ShouldRenderMainHeading()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_WEBSITE_BASE_URL") ?? "http://localhost:5136";

        await Page.GotoAsync(baseUrl);

        var heading = Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Home" });
        await Expect(heading).ToBeVisibleAsync();
    }
}

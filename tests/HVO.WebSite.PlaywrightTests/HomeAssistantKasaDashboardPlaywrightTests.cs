using Microsoft.Playwright;
using System.Text.Json;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class HomeAssistantKasaDashboardPlaywrightTests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task KasaDashboard_ShouldRenderAtDesktopAndMobileWidthsWithoutDeviceActions()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_URL");
        var accessToken = Environment.GetEnvironmentVariable("HOME_ASSISTANT_TOKEN");

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
            Assert.Inconclusive("Set HVO_HOME_ASSISTANT_URL and HOME_ASSISTANT_TOKEN to run the Home Assistant dashboard test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var views = new[]
        {
            (Path: "overview", Heading: "Control Room Infrastructure"),
            (Path: "observatory", Heading: "Roof and Cameras - Status Only"),
            (Path: "infrastructure", Heading: "Control Room - Status Only"),
            (Path: "workshop", Heading: "Workshop Loads"),
            (Path: "utilities", Heading: "Container")
        };

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 1600, Height = 1000 },
                     new ViewportSize { Width = 390, Height = 844 }
                 })
        {
            var context = await browser.NewContextAsync(new()
            {
                ViewportSize = viewport
            });

            var hassUrlJson = JsonSerializer.Serialize(baseUrl.TrimEnd('/'));
            var accessTokenJson = JsonSerializer.Serialize(accessToken);
            await context.AddInitScriptAsync($$"""
                localStorage.setItem('hassTokens', JSON.stringify({
                    hassUrl: {{hassUrlJson}},
                    clientId: `${location.origin}/`,
                    access_token: {{accessTokenJson}},
                    refresh_token: {{accessTokenJson}},
                    expires_in: 86400,
                    expires: Date.now() + 86400000
                }));
                """);

            var page = await context.NewPageAsync();
            foreach (var view in views)
            {
                await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-kasa/{view.Path}");
                await Assertions.Expect(page.GetByText(view.Heading, new() { Exact = true })).ToBeVisibleAsync();

                var bodyMetrics = await page.Locator("body").EvaluateAsync<BodyMetrics>(
                    "body => ({ width: body.clientWidth, scrollWidth: body.scrollWidth })");

                Assert.IsTrue(
                    bodyMetrics.ScrollWidth <= bodyMetrics.Width + 2,
                    $"{view.Path} should not create horizontal overflow at {viewport.Width}px.");
            }

            await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-kasa/overview");
            await Assertions.Expect(page.GetByText("Starlink Router", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("HVO Proxmox 2", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Telescope 1 Total", new() { Exact = true })).ToBeVisibleAsync();

            await context.CloseAsync();
        }
    }

    private sealed class BodyMetrics
    {
        public int Width { get; set; }

        public int ScrollWidth { get; set; }
    }
}

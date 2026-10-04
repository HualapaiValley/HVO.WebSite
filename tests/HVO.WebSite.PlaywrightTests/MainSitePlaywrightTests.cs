using FluentAssertions;
using HVO.WebSite.PlaywrightTests.Infrastructure;
using HVO.WebSite.v9;
using HVO.Edge.Contracts.PowerSystem;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
[TestCategory("Browser")]
public sealed class MainSitePlaywrightTests
{
    public TestContext TestContext { get; set; } = null!;
    private WebsiteBrowserApplication application = null!;
    private BrowserSession session = null!;

    [TestInitialize]
    public async Task Open()
    {
        application = new WebsiteBrowserApplication();
        var observed = application.Clock.GetUtcNow().UtcDateTime;
        var data = WebsitePowerState.Populated(observed);
        application.Power.Data = data with
        {
            Snapshot = data.Snapshot! with
            {
                Pv = data.Snapshot.Pv! with
                {
                    Trackers =
                    [
                        new("eg4-6500ex-a/mppt-1", "Inverter PV 1", "eg4-6500ex-a", "inverter", observed, PowerMetricSource.Eg46500Ex, PowerW: 400),
                        new("eg4-6500ex-a/mppt-2", "Inverter PV 2", "eg4-6500ex-a", "inverter", observed, PowerMetricSource.Eg46500Ex, PowerW: 300),
                        new("eg4-mppt100-48hv-a/mppt-1", "External PV", "eg4-mppt100-48hv-a", "controller", observed, PowerMetricSource.Eg4Mppt10048Hv, PowerW: 300),
                    ],
                    ExpectedTrackerCount = 3, ReportedTrackerCount = 3,
                },
            },
        };
        try { session = await BrowserSession.OpenAsync(TestContext); }
        catch { await application.DisposeAsync(); throw; }
    }

    [TestCleanup]
    public async Task Close()
    {
        try
        {
            if (session is not null)
            {
                try { session.AssertNoUnexpectedErrors(); }
                finally { await session.DisposeAsync(); }
            }
        }
        finally { await application.DisposeAsync(); }
    }

    [TestMethod]
    public async Task MainSite_ShouldRenderPublicHomeShell()
    {
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Hualapai Valley Observatory", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Home" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Current observatory power telemetry")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Home" })).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task MainSite_ShouldShowUnauthenticatedPowerCardMessage()
    {
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));

        await Assertions.Expect(page.Locator(".power-unauthenticated-card")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Live Power Snapshot" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Sign in with observatory access to view the live power-system card.")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task MainSite_AdminRoute_ShouldRedirectUnauthenticatedUserToLogin()
    {
        var page = session.Page;

        await page.RouteAsync("https://login.microsoftonline.com/**", route => route.FulfillAsync(new()
        { Status = 200, ContentType = "text/plain", Body = "Test-owned identity provider landing page" }));
        await page.GotoAsync(BuildUrl("/admin"));

        await Assertions.Expect(page).ToHaveURLAsync(new Regex("https://login.microsoftonline.com/", RegexOptions.IgnoreCase));
        page.Url.Should().Contain("redirect_uri=");
    }

    [TestMethod]
    public async Task MainSite_ShouldToggleThemeWithoutCircuitError()
    {
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();

        await BrowserBehaviorAssertions.SwitchToLightAsync(page);
        await Assertions.Expect(page.Locator(".shell-theme-light")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Switch to dark theme" }).ClickAsync();
        await Assertions.Expect(page.Locator(".shell-theme-dark")).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task MainSite_ShouldRenderSharedThemeCss()
    {
        var requestedUrls = new List<string>();
        var page = session.Page;
        page.Request += (_, request) => requestedUrls.Add(request.Url);

        await page.GotoAsync(BuildUrl("/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-shared-shell.css", StringComparison.OrdinalIgnoreCase));
        requestedUrls.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/css/themes/hvo-components.css", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(page.Locator(".hvo-card").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-eyebrow").First).ToBeVisibleAsync();
        await AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task MainSite_AuthorizedBatteryComparison_ShouldRemainContainedOnDesktopAndMobile()
    {
        var page = session.Page;
        await page.SetViewportSizeAsync(1440, 900);
        await page.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Role"] = AppRoles.User });

        await page.GotoAsync(BuildUrl("/"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Battery Source Comparison" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".power-status-card")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hvo-table-wrap")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("table[aria-label='Battery source observations'] tbody tr").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "PV Inputs" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[aria-label='Canonical site PV inputs'] article")).ToHaveCountAsync(3);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "EG4 Equipment Detail" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#site-pv-history-chart")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#site-eg4-battery-history-chart")).ToBeVisibleAsync();
        await BrowserBehaviorAssertions.ChartInitializedAsync(page, "site-pv-history-chart");
        await AssertThemedSurfaceAsync(page.Locator(".power-status-card"), "power status card");
        await AssertNoPageOverflowAsync(page, "desktop battery comparison");
        await AssertNoBlazorErrorAsync(page);

        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Battery Source Comparison" })).ToBeVisibleAsync();
        var comparisonRegion = page.GetByRole(AriaRole.Region, new() { Name = "Scrollable battery source comparison" });
        await Assertions.Expect(comparisonRegion).ToBeVisibleAsync();
        await comparisonRegion.FocusAsync();
        var regionMetrics = await comparisonRegion.EvaluateAsync<ScrollRegionMetrics>(
            "element => ({ clientWidth: element.clientWidth, scrollWidth: element.scrollWidth, left: element.getBoundingClientRect().left, right: element.getBoundingClientRect().right })");
        regionMetrics.ScrollWidth.Should().BeGreaterThan(regionMetrics.ClientWidth, "the wide comparison should scroll inside its mobile region");
        regionMetrics.Left.Should().BeGreaterThanOrEqualTo(-1);
        regionMetrics.Right.Should().BeLessThanOrEqualTo(391);
        await AssertNoPageOverflowAsync(page, "mobile battery comparison");
        await AssertNoBlazorErrorAsync(page);
    }

    private string BuildUrl(string route) => new Uri(application.Address, route).ToString();

    private static async Task AssertNoBlazorErrorAsync(IPage page) =>
        await Assertions.Expect(page.Locator("#blazor-error-ui")).Not.ToBeVisibleAsync();

    private static async Task AssertNoPageOverflowAsync(IPage page, string label)
    {
        var hasOverflow = await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        hasOverflow.Should().BeFalse($"{label} should contain the comparison table without page-level overlap");
    }

    private static async Task AssertThemedSurfaceAsync(ILocator locator, string label)
    {
        var backgroundImage = await locator.EvaluateAsync<string>("element => getComputedStyle(element).backgroundImage");
        backgroundImage.Should().NotBe("none", $"{label} should use its themed gradient background");
    }

    private sealed class ScrollRegionMetrics
    {
        public int ClientWidth { get; set; }
        public int ScrollWidth { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
    }
}

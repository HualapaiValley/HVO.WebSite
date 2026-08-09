using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class Eg4GatewayPlaywrightTests
{
    private static Process? _simulationHost;
    private static string _baseUrl = string.Empty;

    [ClassInitialize]
    public static async Task StartSimulationHost(TestContext context)
    {
        var root = FindRepositoryRoot();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        _baseUrl = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(Path.Combine(root, "src", "HVO.Hardware.Eg4", "HVO.Hardware.Eg4.csproj"));
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(_baseUrl);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        _simulationHost = Process.Start(start) ?? throw new InvalidOperationException("Could not start EG4 simulation host.");
        _simulationHost.BeginOutputReadLine();
        _simulationHost.BeginErrorReadLine();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_simulationHost.HasExited) Assert.Fail($"EG4 simulation host exited with code {_simulationHost.ExitCode}.");
            try
            {
                if ((await client.GetAsync(_baseUrl)).IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200);
        }
        Assert.Fail("EG4 simulation host did not become healthy within 30 seconds.");
    }

    [ClassCleanup]
    public static void StopSimulationHost()
    {
        if (_simulationHost is null) return;
        if (!_simulationHost.HasExited) _simulationHost.Kill(entireProcessTree: true);
        _simulationHost.Dispose();
    }

    [TestMethod]
    public async Task SimulationDashboard_RendersFleetAndOnlyLocalAssetsOnDesktop()
    {
        var requestedUrls = new List<string>();
        var failedAssets = new ConcurrentQueue<string>();
        var session = await CreatePageAsync(1600, 1000);
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;
        page.Request += (_, request) => requestedUrls.Add(request.Url);
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && (response.Url.Contains("_content/", StringComparison.OrdinalIgnoreCase) ||
                response.Url.Contains("_framework/", StringComparison.OrdinalIgnoreCase) || response.Url.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
                failedAssets.Enqueue($"{response.Status} {response.Url}");
        };

        await page.GotoAsync(BuildUrl("/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "EG4 Battery Branches" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("article.eg4-device-card")).ToHaveCountAsync(3);
        await Assertions.Expect(page.GetByText("Simulator Inverter A", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Simulator MPPT A", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Simulator MPPT B", new() { Exact = true })).ToBeVisibleAsync();
        var sourceOrder = await page.Locator("article.eg4-device-card").EvaluateAllAsync<string[]>(
            "cards => cards.map(card => card.getAttribute('data-source-id'))");
        sourceOrder.Should().Equal("eg4-sim-inverter-a", "eg4-sim-mppt-a", "eg4-sim-mppt-b");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh view" }).ClickAsync();
        failedAssets.Should().BeEmpty();
        await AssertThemedSurfaceAsync(page.Locator("article.eg4-device-card").First, "EG4 device card");
        await PlaywrightGatewayAssertions.AssertNoCdnResourcesAsync(requestedUrls);
        await PlaywrightGatewayAssertions.AssertLocalThemeResourcesLoadedAsync(requestedUrls);
        await PlaywrightGatewayAssertions.AssertNoLegacyClassesAsync(page, "EG4 dashboard");
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);
    }

    [TestMethod]
    public async Task SimulationDashboard_RemainsUsableAtMobileWidth()
    {
        var session = await CreatePageAsync(390, 844);
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;

        await page.GotoAsync(BuildUrl("/"));

        await Assertions.Expect(page.Locator("article.eg4-device-card").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Batch size (1-500)")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Apply runtime settings" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Apply runtime settings" })).ToBeDisabledAsync();
        await AssertThemedControlAsync(page.GetByLabel("Batch size (1-500)"));
        var overflow = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth");
        overflow.Should().BeFalse();
        await PlaywrightGatewayAssertions.AssertNoBlazorErrorAsync(page);
    }

    private static async Task<(IPlaywright Playwright, IBrowser Browser, IPage Page)> CreatePageAsync(int width, int height)
    {
        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = width, Height = height } });
            return (playwright, browser, page);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    private static string BuildUrl(string route)
    {
        return new Uri(new Uri(_baseUrl.TrimEnd('/') + "/"), route.TrimStart('/')).ToString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HVO.WebSite.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the HVO.WebSite solution root.");
    }

    private static async Task AssertThemedSurfaceAsync(ILocator locator, string label)
    {
        var background = await locator.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor");
        background.Should().NotBe("rgba(0, 0, 0, 0)", $"{label} should use a themed background").And.NotBe("transparent");
    }

    private static async Task AssertThemedControlAsync(ILocator locator)
    {
        var colors = await locator.EvaluateAsync<string>(
            "element => { const style = getComputedStyle(element); return `${style.color}|${style.backgroundColor}`; }");
        colors.Should().NotContain("rgb(0, 0, 0)|rgb(255, 255, 255)", "numeric controls should not render with the browser-default black-on-white theme");
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class Eg4GatewayPlaywrightTests
{
    private static Process? _simulationHost;
    private static string _baseUrl = string.Empty;
    private static string? _outboxPath;

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
        _outboxPath = Path.Combine(Path.GetTempPath(), $"eg4-playwright-{Guid.NewGuid():N}.db");
        start.Environment["Outbox__DbPath"] = _outboxPath;
        start.Environment["Outbox__ApiKey"] = "playwright-diagnostics-key";
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
        if (_outboxPath is not null)
        {
            try { File.Delete(_outboxPath); } catch (IOException) { }
        }
    }

    [TestMethod]
    public async Task SimulationDashboard_RendersFleetAndOnlyLocalAssetsOnDesktop()
    {
        var requestedUrls = new ConcurrentQueue<string>();
        var failedAssets = new ConcurrentQueue<string>();
        var session = await CreatePageAsync(1600, 1000);
        using var playwright = session.Playwright;
        await using var browser = session.Browser;
        var page = session.Page;
        page.Request += (_, request) => requestedUrls.Enqueue(request.Url);
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && (response.Url.Contains("_content/", StringComparison.OrdinalIgnoreCase) ||
                response.Url.Contains("_framework/", StringComparison.OrdinalIgnoreCase) || response.Url.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
                failedAssets.Enqueue($"{response.Status} {response.Url}");
        };

        await page.GotoAsync(BuildUrl("/"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "EG4 Solar & Battery" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("article.eg4-device-card")).ToHaveCountAsync(2);
        await Assertions.Expect(page.GetByText("Simulator Inverter A", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Simulator Inverter B", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Simulator Inverter A PV inputs" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Simulator Inverter A inverter detail" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#eg4-pv-power-chart")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#eg4-battery-power-chart")).ToBeVisibleAsync();
        (await page.Locator("#eg4-pv-power-chart").EvaluateAsync<bool>(
            "canvas => canvas.clientWidth > 0 && canvas.clientHeight > 0 && Boolean(window.Chart?.getChart(canvas.id))"))
            .Should().BeTrue("Chart.js should create the live PV chart instance");
        (await page.Locator("#eg4-battery-power-chart").EvaluateAsync<bool>(
            "canvas => canvas.clientWidth > 0 && canvas.clientHeight > 0 && Boolean(window.Chart?.getChart(canvas.id))"))
            .Should().BeTrue("Chart.js should create the live battery chart instance");
        var sourceOrder = await page.Locator("article.eg4-device-card").EvaluateAllAsync<string[]>(
            "cards => cards.map(card => card.getAttribute('data-source-id'))");
        sourceOrder.Should().Equal("eg4-sim-inverter-a", "eg4-sim-inverter-b");
        (await WaitForPendingOutboxCountAsync()).Should().BeGreaterThanOrEqualTo(2,
            "the simulator must exercise the same durable collection path as production");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh view" }).ClickAsync();
        failedAssets.Should().BeEmpty();
        await AssertThemedSurfaceAsync(page.Locator("article.eg4-device-card").First, "EG4 device card");
        var requestedUrlSnapshot = requestedUrls.ToArray();
        await PlaywrightGatewayAssertions.AssertNoCdnResourcesAsync(requestedUrlSnapshot);
        await PlaywrightGatewayAssertions.AssertLocalThemeResourcesLoadedAsync(requestedUrlSnapshot);
        requestedUrlSnapshot.Should().Contain(url => url.Contains("_content/HVO.WebSite.Themes/js/chart.min.js", StringComparison.OrdinalIgnoreCase));
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
        await Assertions.Expect(page.GetByLabel("Batch size (1-500)")).ToBeEnabledAsync();
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

    private static async Task<int> WaitForPendingOutboxCountAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        client.DefaultRequestHeaders.Add("X-Api-Key", "playwright-diagnostics-key");
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var response = await client.GetAsync("/diagnostics/outbox");
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var count = json.RootElement.GetProperty("pendingCount").GetInt32();
                if (count >= 2) return count;
            }
            await Task.Delay(100);
        }
        return 0;
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

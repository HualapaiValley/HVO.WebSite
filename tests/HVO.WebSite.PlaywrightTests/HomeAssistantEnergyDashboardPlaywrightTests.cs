using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Playwright;
using System.Text.Json;

namespace HVO.WebSite.PlaywrightTests;

[TestClass]
public sealed class HomeAssistantEnergyDashboardPlaywrightTests
{
    private const double PowerToleranceWatts = 1.0;
    private const double DailyEnergyToleranceKwh = 0.002;

    [TestMethod]
    [TestCategory("Live")]
    public async Task EnergyDashboard_ShouldRenderKeyGaugesAndEveryViewWithoutErrorsOrOverflow()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_URL");
        var accessToken = Environment.GetEnvironmentVariable("HOME_ASSISTANT_TOKEN");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
            Assert.Inconclusive("Set HVO_HOME_ASSISTANT_URL and HOME_ASSISTANT_TOKEN to run the Home Assistant Energy dashboard test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 1600, Height = 1000 },
                     new ViewportSize { Width = 390, Height = 844 }
                 })
        {
            await using var context = await browser.NewContextAsync(new() { ViewportSize = viewport });
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
            foreach (var (path, heading) in new[]
                     {
                         ("overview", "Live Power"),
                         ("generation", "Generation Now"),
                         ("usage", "6500EX AC Load"),
                         ("batteries", "Whole-Bus Battery"),
                         ("6500ex", "6500EX Generation"),
                         ("mppt100", "Standalone MPPT100"),
                         ("smartshunt", "SmartShunt Whole-Bus Meter"),
                         ("jk-health", "System Health"),
                     })
            {
                await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-energy/{path}");
                await Assertions.Expect(page.GetByText(heading, new() { Exact = true }).First).ToBeVisibleAsync();
                await Assertions.Expect(page.Locator("hui-error-card")).ToHaveCountAsync(0);
                await Assertions.Expect(page.GetByText("Configuration error", new() { Exact = false })).ToHaveCountAsync(0);
                await AssertNoHorizontalOverflowAsync(page, viewport.Width, path);
            }

            await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-energy/overview");
            await Assertions.Expect(page.GetByText("All Three PV Total", new() { Exact = true }).First).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("6500EX Native Subtotal", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("All-Three Calculated Total", new() { Exact = true })).ToBeVisibleAsync();

            await page.GotoAsync($"{baseUrl.TrimEnd('/')}/hvo-energy/jk-health");
            await Assertions.Expect(page.GetByText("Bank 1 SOC", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Bank 7 SOC", new() { Exact = true })).ToBeVisibleAsync();
        }
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task EnergyHelpers_ShouldNumericallyAgreeWithTheirSourceEntities()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HVO_HOME_ASSISTANT_URL");
        var accessToken = Environment.GetEnvironmentVariable("HOME_ASSISTANT_TOKEN");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
            Assert.Inconclusive("Set HVO_HOME_ASSISTANT_URL and HOME_ASSISTANT_TOKEN to run the Home Assistant numerical test.");

        using var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var states = await client.GetFromJsonAsync<HomeAssistantState[]>("api/states")
            ?? throw new AssertFailedException("Home Assistant returned no states.");
        var powerSources = new[]
        {
            "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f1_x5fpower",
            "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f2_x5fpower",
            "sensor.hvo_3xhvo_3xeg4_15xcontroller_x2da_24xpv_x5fmppt_x5f1_x5fpower",
        };
        var dailySources = new[] { "sensor.hvo_6500ex_pv_energy_daily", "sensor.hvo_external_mppt_pv_energy_daily" };
        var powerHelper = "sensor.hvo_all_pv_power";
        var dailyHelper = "sensor.hvo_total_pv_energy_daily";
        var required = powerSources.Concat(dailySources).Append(powerHelper).Append(dailyHelper).ToHashSet(StringComparer.Ordinal);
        var relevant = states.Where(state => required.Contains(state.EntityId))
            .ToDictionary(state => state.EntityId, StringComparer.Ordinal);
        relevant.Keys.Should().BeEquivalentTo(required, "every aggregate source and helper must exist");

        AssertAggregate(powerSources, powerHelper, relevant, PowerToleranceWatts, "All-three PV power");
        AssertAggregate(dailySources, dailyHelper, relevant, DailyEnergyToleranceKwh, "Combined daily PV");
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page, int viewportWidth, string view)
    {
        var metrics = await page.Locator("body").EvaluateAsync<BodyMetrics>(
            "body => ({ width: body.clientWidth, scrollWidth: body.scrollWidth })");
        Assert.IsTrue(metrics.ScrollWidth <= metrics.Width + 2,
            $"{view} should not create horizontal overflow at {viewportWidth}px.");
    }

    private sealed class BodyMetrics
    {
        public int Width { get; set; }
        public int ScrollWidth { get; set; }
    }

    private static void AssertAggregate(
        IReadOnlyList<string> sourceIds,
        string helperId,
        IReadOnlyDictionary<string, HomeAssistantState> states,
        double tolerance,
        string description)
    {
        var sources = sourceIds.Select(id => states[id]).ToArray();
        var helper = states[helperId];
        if (sources.Any(source => !TryNumericState(source, out _)))
        {
            Assert.AreEqual("unavailable", helper.State,
                $"{description} helper must be unavailable when any source is non-numeric.");
            return;
        }

        Assert.IsTrue(TryNumericState(helper, out var actual), $"{helperId} must be numeric when every source is numeric.");
        var expected = sources.Sum(source => double.Parse(source.State, NumberStyles.Float, CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, actual, tolerance, $"{description} must agree within {tolerance}.");
    }

    private static bool TryNumericState(HomeAssistantState state, out double value) =>
        double.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private sealed record HomeAssistantState(
        [property: System.Text.Json.Serialization.JsonPropertyName("entity_id")] string EntityId,
        [property: System.Text.Json.Serialization.JsonPropertyName("state")] string State);
}

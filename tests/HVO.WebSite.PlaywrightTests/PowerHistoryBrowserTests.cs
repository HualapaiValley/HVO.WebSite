using System.Text.Json;
using FluentAssertions;
using HVO.WebSite.PlaywrightTests.Infrastructure;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Models;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests;

[TestClass, TestCategory("Browser")]
public sealed class PowerHistoryBrowserTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(DateTimeKind.Utc), DataRow(DateTimeKind.Unspecified)]
    public async Task ActualPowerCharts_PreserveSharedPartialLeadingTrailingGaps_AndProportionalUtcSpacing(DateTimeKind storedKind)
    {
        await using var application = new WebsiteBrowserApplication();
        var end = application.Clock.GetUtcNow().UtcDateTime;
        var start = end.AddHours(-6);
        var details = new List<PowerMpptDetailSnapshotResponse>();
        foreach (var index in new[] { 1, 2, 15, 71 })
        {
            var at = DateTime.SpecifyKind(start.AddMinutes(index * 5), storedKind);
            details.Add(new() { SourceId = "eg4-6500ex-a", RecordedAtUtc = at,
                Trackers = [new() { TrackerId = "mppt-1", PowerW = 100 }, new() { TrackerId = "mppt-2", PowerW = 200 }] });
            if (index != 2)
                details.Add(new() { SourceId = "eg4-mppt100-48hv-a", RecordedAtUtc = at,
                    Trackers = [new() { TrackerId = "mppt-1", PowerW = 50 }] });
        }
        var history = new PowerTelemetryHistoryResponse(details,
            [new(DateTime.SpecifyKind(start.AddMinutes(5), storedKind), "eg4-6500ex-a", "inverter", -100),
             new(DateTime.SpecifyKind(start.AddMinutes(75), storedKind), "eg4-6500ex-a", "inverter", 200)]);
        application.Power.Data = WebsitePowerState.Populated(end) with { History = history };
        await using var browser = await BrowserSession.OpenAsync(TestContext);
        var page = browser.Page;
        await page.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Role"] = AppRoles.User });
        await page.GotoAsync(application.Address.ToString());
        await Assertions.Expect(page.GetByText("Last 6 hour(s) · America/Phoenix", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Partial coverage; gaps indicate missing observations. Five-minute intervals are aligned in UTC.", new() { Exact = true })).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!window.Chart?.getChart('site-pv-history-chart') && !!window.Chart?.getChart('site-eg4-battery-history-chart')");
        var actual = await page.EvaluateAsync<JsonElement>("""
            () => {
                const chart = Chart.getChart('site-pv-history-chart');
                const battery = Chart.getChart('site-eg4-battery-history-chart');
                const data = chart.data.datasets;
                const meta = chart.getDatasetMeta(0);
                const subtotal = data.find(dataset => dataset.label === '5-minute PV subtotal');
                const spacing = meta.data[3].x - meta.data[2].x;
                return {
                    count: chart.data.labels.length,
                    firstLabel: chart.data.labels[0], lastLabel: chart.data.labels.at(-1),
                    leading: data[0].data[0], trailing: data[0].data[72],
                    globalOutage: data.every(dataset => dataset.data.slice(3, 15).every(value => value === null)),
                    partialA: data[0].data[2], partialC: data[2].data[2], partialSubtotal: subtotal.data[2],
                    completeSubtotal: subtotal.data[1], nullPointSkipped: meta.data[3].skip,
                    bridgesGap: meta.dataset.segments.some(segment => segment.start <= 2 && segment.end >= 15),
                    spacingRatio: (meta.data[15].x - meta.data[2].x) / spacing,
                    batteryCharge: battery.data.datasets[0].data[1], batteryDischarge: battery.data.datasets[0].data[15],
                    aligned: data.every(dataset => dataset.data.length === 73) && battery.data.labels.length === 73
                };
            }
            """);
        actual.GetProperty("count").GetInt32().Should().Be(73);
        actual.GetProperty("firstLabel").GetString().Should().Be("11:00");
        actual.GetProperty("lastLabel").GetString().Should().Be("17:00");
        actual.GetProperty("leading").ValueKind.Should().Be(JsonValueKind.Null);
        actual.GetProperty("trailing").ValueKind.Should().Be(JsonValueKind.Null);
        actual.GetProperty("globalOutage").GetBoolean().Should().BeTrue();
        actual.GetProperty("partialA").GetDouble().Should().Be(100);
        actual.GetProperty("partialC").ValueKind.Should().Be(JsonValueKind.Null);
        actual.GetProperty("partialSubtotal").ValueKind.Should().Be(JsonValueKind.Null);
        actual.GetProperty("completeSubtotal").GetDouble().Should().Be(350);
        actual.GetProperty("nullPointSkipped").GetBoolean().Should().BeTrue();
        actual.GetProperty("bridgesGap").GetBoolean().Should().BeFalse();
        actual.GetProperty("spacingRatio").GetDouble().Should().BeApproximately(13, 0.001);
        actual.GetProperty("batteryCharge").GetDouble().Should().Be(100);
        actual.GetProperty("batteryDischarge").GetDouble().Should().Be(-200);
        actual.GetProperty("aligned").GetBoolean().Should().BeTrue();
        browser.AssertNoUnexpectedErrors();
    }
}

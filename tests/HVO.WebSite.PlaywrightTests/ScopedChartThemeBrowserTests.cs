using FluentAssertions;
using HVO.WebSite.PlaywrightTests.Infrastructure;

namespace HVO.WebSite.PlaywrightTests;

[TestClass, TestCategory("Browser"), DoNotParallelize]
public sealed class ScopedChartThemeBrowserTests
{
    private static BrowserApplication<HVO.ThemeSandbox.Components.App> application = null!;
    private BrowserSession session = null!;
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize, Timeout(30000)]
    public static void Start(TestContext _) => application = new("HVO.ThemeSandbox");

    [ClassCleanup]
    public static async Task Stop() => await application.DisposeAsync();

    [TestInitialize]
    public async Task Open()
    {
        session = await BrowserSession.OpenAsync(TestContext);
        await session.Page.AddInitScriptAsync("""
            window.chartObservers = new Set();
            const observe = MutationObserver.prototype.observe;
            const disconnect = MutationObserver.prototype.disconnect;
            MutationObserver.prototype.observe = function(...args) {
                if ((new Error().stack.split('\n')[2] || '').includes('/hvo-chart.js')) window.chartObservers.add(this);
                return observe.apply(this,args);
            };
            MutationObserver.prototype.disconnect = function() {
                window.chartObservers.delete(this);
                return disconnect.call(this);
            };
            """);
        await session.Page.GotoAsync(application.Address.ToString());
        await Ready();
    }

    [TestCleanup]
    public async Task Close() => await session.DisposeAsync();

    private async Task Ready() => await session.Page.WaitForFunctionAsync("() => Object.keys(window.hvoChart?._instances || {}).length === 3");

    private async Task Toggle(bool light)
    {
        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = light ? "Switch to light theme" : "Switch to dark theme" }).ClickAsync();
        await session.Page.WaitForFunctionAsync("light => document.querySelector(light ? '.shell-theme-light' : '.shell-theme-dark') !== null", light);
        await AssertColors();
    }

    private async Task AssertColors()
    {
        await session.Page.WaitForFunctionAsync("""
            () => {
                const chart = hvoChart._instances['sandbox-chart-themed'];
                const colors = hvoChart.getThemeColors(chart.canvas);
                return chart.options.plugins.legend.labels.color === colors.labelColor;
            }
            """);
        var errors = await session.Page.EvaluateAsync<string[]>("""
            () => {
                const chart = hvoChart._instances['sandbox-chart-themed'];
                const colors = hvoChart.getThemeColors(chart.canvas);
                const errors = [];
                const check = (name,a,b) => { if (JSON.stringify(a) !== JSON.stringify(b)) errors.push(name+': '+JSON.stringify(a)+' != '+JSON.stringify(b)); };
                for (const [name,scale] of Object.entries(chart.options.scales)) {
                    check(name+' grid',scale.grid.color,colors.gridColor);
                    check(name+' border',scale.border.color,colors.axisColor);
                    check(name+' ticks',scale.ticks.color,colors.labelColor);
                    check(name+' title',scale.title.color,colors.labelColor);
                }
                check('axes',Object.keys(chart.options.scales).sort(),['x','y','yF']);
                check('legend',chart.options.plugins.legend.labels.color,colors.labelColor);
                check('title',chart.options.plugins.title.color,colors.labelColor);
                check('default border',chart.data.datasets[0].borderColor,colors.labelColor);
                check('default background',chart.data.datasets[0].backgroundColor,colors.markerFill);
                check('scalar border',chart.data.datasets[1].borderColor,colors.axisColor);
                check('scalar background',chart.data.datasets[1].backgroundColor,colors.markerFill);
                check('array border',chart.data.datasets[2].borderColor,[colors.labelColor,colors.axisColor,colors.labelColor,colors.axisColor]);
                check('array background',chart.data.datasets[2].backgroundColor,[colors.markerFill,colors.labelColor,colors.markerFill,colors.labelColor]);
                const literal = hvoChart._instances['sandbox-chart-dense'].data.datasets[0];
                check('literal border',literal.borderColor,'#69d3ff');
                check('literal background',literal.backgroundColor,'rgba(105,211,255,0.15)');
                return errors;
            }
            """);
        errors.Should().BeEmpty();
        await session.Page.Locator("#sandbox-chart-themed").ScrollIntoViewIfNeededAsync();
    }

    [TestMethod, Timeout(30000)]
    [DataRow(false), DataRow(true)]
    public async Task InitialChartsResolveCanvasScope(bool light)
    {
        if (light)
        {
            await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Palette", Exact = true }).ClickAsync();
            await TogglePaletteLight();
            await session.Page.GoBackAsync();
            await Ready();
        }
        await AssertColors();
        (await session.Page.EvaluateAsync<bool>("() => hvoChart.getThemeColors(hvoChart._instances['sandbox-chart-themed'].canvas).labelColor !== hvoChart.getThemeColors(document.documentElement).labelColor")).Should().Be(light);
        session.AssertNoUnexpectedErrors();
    }

    private async Task TogglePaletteLight() => await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Switch to light theme" }).ClickAsync();

    [TestMethod, Timeout(30000)]
    public async Task ThemeRoundTripPreservesInstanceDataAndNullGaps()
    {
        await session.Page.EvaluateAsync("""
            () => {
                window.originalChart = hvoChart._instances['sandbox-chart-themed'];
                window.originalData = JSON.stringify(originalChart.data.datasets.map(d => d.data));
                window.themeUpdates = 0;
                const update = originalChart.update;
                originalChart.update = function(...args) { window.themeUpdates++; return update.apply(this,args); };
            }
            """);
        await Toggle(true);
        await Toggle(false);
        await session.Page.EvaluateAsync("() => document.querySelector('.shell-theme-dark').classList.add('unrelated-test-class')");
        await session.Page.WaitForTimeoutAsync(100);
        (await session.Page.EvaluateAsync<bool>("() => originalChart === hvoChart._instances['sandbox-chart-themed'] && originalData === JSON.stringify(originalChart.data.datasets.map(d => d.data)) && themeUpdates === 2")).Should().BeTrue();
        (await session.Page.EvaluateAsync<bool>("""
            () => {
                const meta = originalChart.getDatasetMeta(0);
                return JSON.stringify(meta.data.map(point => point.skip)) === '[false,true,false,false]' &&
                    meta.dataset.segments.length === 2 && meta.dataset.segments.every(segment => !(segment.start < 1 && segment.end > 1));
            }
            """)).Should().BeTrue("real Chart.js must preserve the missing observation as a line gap");
        session.AssertNoUnexpectedErrors();
    }

    [TestMethod, Timeout(30000)]
    public async Task NavigationDestroysChartsAndObserversAndRecreatesThem()
    {
        for (var iteration = 0; iteration < 2; iteration++)
        {
            (await session.Page.EvaluateAsync<int>("() => chartObservers.size")).Should().Be(3);
            await session.Page.EvaluateAsync("() => window.departedChart = hvoChart._instances['sandbox-chart-themed']");
            await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Palette", Exact = true }).ClickAsync();
            await session.Page.WaitForFunctionAsync("() => Object.keys(hvoChart._instances).length === 0 && Object.keys(hvoChart._themes).length === 0 && chartObservers.size === 0 && departedChart.canvas === null");
            await session.Page.GoBackAsync();
            await Ready();
            (await session.Page.EvaluateAsync<bool>("() => departedChart !== hvoChart._instances['sandbox-chart-themed']")).Should().BeTrue();
        }
        await AssertColors();
        session.AssertNoUnexpectedErrors();
    }

    [TestMethod, Timeout(30000)]
    public async Task FailedRealConstructorReleasesPartialChartAndCanRecover()
    {
        (await session.Page.EvaluateAsync<bool>("""
            () => {
                const id = 'sandbox-chart-themed';
                const original = hvoChart._instances[id];
                const config = hvoChart.stripNulls(original.config._config);
                hvoChart._themes[id].datasetColors.forEach((colors,i) => Object.assign(config.data.datasets[i],colors));
                hvoChart.render(id,{type:'missing-controller',data:{labels:['1'],datasets:[{data:[1]}]},options:{}});
                const cleaned = !Chart.getChart(document.getElementById(id)) && !hvoChart._instances[id] && !hvoChart._themes[id] && original.canvas === null && chartObservers.size === 2;
                hvoChart.render(id,config);
                return cleaned && !!hvoChart._instances[id] && chartObservers.size === 3;
            }
            """)).Should().BeTrue();
        await AssertColors();
        await Toggle(true);
        session.AssertNoUnexpectedErrors("[HvoChart] render failed for \"sandbox-chart-themed\":");
    }
}

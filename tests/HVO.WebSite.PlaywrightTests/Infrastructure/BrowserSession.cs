using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

/// <summary>Isolated browser/context plus bounded actions and retained diagnostics.</summary>
internal sealed class BrowserSession : IAsyncDisposable
{
    private readonly IPlaywright playwright;
    private readonly IBrowser browser;
    private readonly IBrowserContext context;
    private readonly TestContext testContext;
    private readonly ConcurrentQueue<string> messages = new();
    private readonly ConcurrentQueue<string> errors = new();

    private BrowserSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page, TestContext testContext)
    {
        this.playwright = playwright;
        this.browser = browser;
        this.context = context;
        this.testContext = testContext;
        Page = page;
        var root = Environment.GetEnvironmentVariable("HVO_BROWSER_ARTIFACTS")
            ?? Path.Combine(BrowserApplication<object>.RepositoryRoot, "TestResults", "browser");
        var name = string.Concat((testContext.TestName ?? "browser").Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        ArtifactDirectory = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ArtifactDirectory);
        page.Console += (_, message) =>
        {
            messages.Enqueue($"{message.Type}: {message.Text}");
            if (message.Type == "error") errors.Enqueue(message.Text);
        };
        page.PageError += (_, message) => errors.Enqueue(message);
        page.SetDefaultTimeout(8000);
        page.SetDefaultNavigationTimeout(10000);
    }

    public IPage Page { get; }
    public string ArtifactDirectory { get; }

    public static async Task<BrowserSession> OpenAsync(TestContext testContext)
    {
        var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Timeout = 10000 });
            var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1600, Height = 1000 } });
            await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
            var page = await context.NewPageAsync();
            return new(playwright, browser, context, page, testContext);
        }
        catch
        {
            if (browser is not null) await browser.DisposeAsync();
            playwright.Dispose();
            throw;
        }
    }

    public void AssertNoUnexpectedErrors(string? allowedPrefix = null) =>
        errors.Where(message => allowedPrefix is null || !message.StartsWith(allowedPrefix, StringComparison.Ordinal))
            .Should().BeEmpty("the browser/circuit must remain healthy");

    public async ValueTask DisposeAsync()
    {
        try
        {
            var console = Path.Combine(ArtifactDirectory, "console.log");
            await File.WriteAllLinesAsync(console, messages.Concat(errors));
            testContext.AddResultFile(console);
            if (!Page.IsClosed)
            {
                var screenshot = Path.Combine(ArtifactDirectory, testContext.CurrentTestOutcome == UnitTestOutcome.Passed ? "page.png" : "failure.png");
                await Page.ScreenshotAsync(new() { Path = screenshot, FullPage = true, Timeout = 5000 });
                testContext.AddResultFile(screenshot);
            }
            var trace = Path.Combine(ArtifactDirectory, "trace.zip");
            await context.Tracing.StopAsync(new() { Path = trace }).WaitAsync(TimeSpan.FromSeconds(10));
            testContext.AddResultFile(trace);
        }
        finally
        {
            await browser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            playwright.Dispose();
        }
    }
}

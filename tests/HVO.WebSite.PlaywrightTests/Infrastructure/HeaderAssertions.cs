using FluentAssertions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

internal static class HeaderAssertions
{
    public static async Task ReachableAsync(IPage page)
    {
        await page.EvaluateAsync("document.fonts.ready");
        await page.EvaluateAsync("async () => { await Promise.all(document.getAnimations().map(a => a.finished.catch(() => {}))); }");
        var header = page.Locator(".shell-header-frame");
        var headerBox = (await header.BoundingBoxAsync())!;
        var canvasBox = (await page.Locator(".shell-content > .layout-canvas").BoundingBoxAsync())!;
        canvasBox.Y.Should().BeGreaterThanOrEqualTo(headerBox.Y + headerBox.Height,
            "wrapped header rows must reserve space above page content");
        canvasBox.Height.Should().BeGreaterThan(0);
        var elements = header.Locator(".shell-brand-text, .shell-brand-subtitle, a, button");
        var boxes = new List<LocatorBoundingBoxResult>();
        for (var i = 0; i < await elements.CountAsync(); i++)
        {
            var element = elements.Nth(i);
            await Assertions.Expect(element).ToBeVisibleAsync();
            var box = (await element.BoundingBoxAsync())!;
            box.X.Should().BeGreaterThanOrEqualTo(0);
            (box.X + box.Width).Should().BeLessThanOrEqualTo(page.ViewportSize!.Width + 1);
            box.Y.Should().BeGreaterThanOrEqualTo(0);
            (box.Y + box.Height).Should().BeLessThanOrEqualTo(page.ViewportSize.Height);
            foreach (var other in boxes)
                (box.X < other.X + other.Width - 1 && box.X + box.Width > other.X + 1
                    && box.Y < other.Y + other.Height - 1 && box.Y + box.Height > other.Y + 1)
                    .Should().BeFalse("header text and controls must not intersect");
            boxes.Add(box);
            if (await element.EvaluateAsync<bool>("e => e.matches('a,button')"))
            {
                await element.ClickAsync(new() { Trial = true });
                await element.FocusAsync();
                await Assertions.Expect(element).ToBeFocusedAsync();
                await page.Keyboard.PressAsync("Tab");
                await Assertions.Expect(element).Not.ToBeFocusedAsync();
            }
        }
    }
}

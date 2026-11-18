using FluentAssertions;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

internal static class HeaderAssertions
{
    public static async Task SettleAsync(IPage page)
    {
        await page.EvaluateAsync("document.fonts.ready");
        await page.EvaluateAsync("async () => { await Promise.all(document.getAnimations().filter(a => a.effect?.getTiming().iterations !== Infinity).map(a => a.finished.catch(() => {}))); }");
    }

    public static async Task TextAndControlsAsync(IPage page)
    {
        await SettleAsync(page);
        var elements = page.Locator(".shell-header-frame").Locator(".shell-brand-text, .shell-brand-subtitle, .shell-brand-mark, a, button");
        var boxes = new List<(LocatorBoundingBoxResult Box, string Name, ILocator Element, bool Text)>();
        for (var i = 0; i < await elements.CountAsync(); i++)
        {
            var element = elements.Nth(i);
            await Assertions.Expect(element).ToBeVisibleAsync();
            var name = await element.EvaluateAsync<string>("e => e.className + ': ' + (e.textContent || e.getAttribute('aria-label') || '')");
            var text = await element.EvaluateAsync<bool>("e => e.matches('.shell-brand-text,.shell-brand-subtitle')");
            var box = text
                ? await element.EvaluateAsync<LocatorBoundingBoxResult>("e => { const r = document.createRange(); r.selectNodeContents(e); const b = r.getBoundingClientRect(); return { x:b.x, y:b.y, width:b.width, height:b.height }; }")
                : await RequiredBoxAsync(element, name);
            boxes.Add((box, name, element, text));
        }
        // Compare all painted text first, so a title/nav collision is reported
        // even when the same title also overflows its shrunken CSS box.
        for (var i = 0; i < boxes.Count; i++)
        for (var j = i + 1; j < boxes.Count; j++)
        {
            var (box, name, _, _) = boxes[i];
            var (other, otherName, _, _) = boxes[j];
            (box.X < other.X + other.Width - 1 && box.X + box.Width > other.X + 1
                && box.Y < other.Y + other.Height - 1 && box.Y + box.Height > other.Y + 1)
                .Should().BeFalse($"{name} must not intersect {otherName}");
        }
        foreach (var (box, name, element, text) in boxes)
        {
            box.X.Should().BeGreaterThanOrEqualTo(0, name);
            (box.X + box.Width).Should().BeLessThanOrEqualTo(page.ViewportSize!.Width + 1, name);
            box.Y.Should().BeGreaterThanOrEqualTo(0, name);
            (box.Y + box.Height).Should().BeLessThanOrEqualTo(page.ViewportSize.Height, name);
            if (text)
                (await element.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1"))
                    .Should().BeTrue($"{name} must not clip or overflow its own box");
        }
    }

    public static async Task ReachableAsync(IPage page)
    {
        await TextAndControlsAsync(page);
        var headerBox = await RequiredBoxAsync(page.Locator(".shell-header-frame"), "shared header");
        var canvasBox = await RequiredBoxAsync(page.Locator(".shell-content > .layout-canvas"), "page canvas");
        canvasBox.Y.Should().BeGreaterThanOrEqualTo(headerBox.Y + headerBox.Height,
            "wrapped header rows must reserve space above page content");
        canvasBox.Height.Should().BeGreaterThan(0);
        foreach (var element in await page.Locator(".shell-header-frame a, .shell-header-frame button").AllAsync())
        {
            await element.ClickAsync(new() { Trial = true });
            await element.FocusAsync();
            await Assertions.Expect(element).ToBeFocusedAsync();
        }
    }

    public static async Task DrawerReachableAsync(IPage page)
    {
        await SettleAsync(page);
        var drawer = page.Locator(".mud-drawer");
        await Assertions.Expect(drawer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("mud-drawer--open"));
        var targets = drawer.Locator(".mud-drawer-header, .mud-nav-link");
        for (var i = 0; i < await targets.CountAsync(); i++)
        {
            var target = targets.Nth(i);
            await target.ScrollIntoViewIfNeededAsync();
            (await target.EvaluateAsync<bool>("""
                e => {
                    const d = e.closest('.mud-drawer'), b = e.getBoundingClientRect(), r = d.getBoundingClientRect();
                    const x = b.x + b.width/2, y = b.y + b.height/2;
                    const hit = document.elementFromPoint(x, y);
                    return x >= r.left && x <= r.right && y >= r.top && y <= r.bottom
                        && d.contains(hit);
                }
                """)).Should().BeTrue($"drawer item {await target.InnerTextAsync()} must be inside the drawer and uncovered");
        }
        await drawer.EvaluateAsync("e => e.scrollTop = 0");
    }

    public static async Task NavigateAsync(IPage page, ILocator link, string destination)
    {
        new Uri(page.Url).AbsolutePath.Should().NotBe(destination, "navigation must change the route");
        await link.PressAsync("Enter");
        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(destination) + "$"));
    }

    private static async Task<LocatorBoundingBoxResult> RequiredBoxAsync(ILocator element, string name) =>
        await element.BoundingBoxAsync()
            ?? throw new AssertFailedException($"Expected a visible bounding box for {name}; the element may have detached or become hidden.");
}

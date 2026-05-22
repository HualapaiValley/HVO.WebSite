using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class SiteConfigurationServiceTests
{
    private static HvoV9DbContext CreateDbContext(string name) =>
        new(new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static IMemoryCache CreateCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private static SiteConfigurationService CreateService(HvoV9DbContext db, IMemoryCache cache) =>
        new(db, cache, NullLogger<SiteConfigurationService>.Instance);

    [TestMethod]
    public async Task GetValueAsync_ReturnsStoredValue()
    {
        await using var db = CreateDbContext(nameof(GetValueAsync_ReturnsStoredValue));
        db.SiteConfiguration.Add(new SiteConfiguration
        {
            Key = "ui:site-title",
            Value = "Hualapai Valley Observatory",
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        using var cache = CreateCache();
        var service = CreateService(db, cache);

        var value = await service.GetValueAsync("ui:site-title");

        value.Should().Be("Hualapai Valley Observatory");
    }

    [TestMethod]
    public async Task GetValueAsync_UsesCachedSnapshot_UntilInvalidated()
    {
        await using var db = CreateDbContext(nameof(GetValueAsync_UsesCachedSnapshot_UntilInvalidated));
        db.SiteConfiguration.Add(new SiteConfiguration
        {
            Key = "feature:beta-dashboard",
            Value = "false",
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        using var cache = CreateCache();
        var service = CreateService(db, cache);

        var initialValue = await service.GetValueAsync("feature:beta-dashboard");

        var entry = await db.SiteConfiguration.SingleAsync(x => x.Key == "feature:beta-dashboard");
        entry.Value = "true";
        entry.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var cachedValue = await service.GetValueAsync("feature:beta-dashboard");
        service.Invalidate("feature:beta-dashboard");
        var refreshedValue = await service.GetValueAsync("feature:beta-dashboard");

        initialValue.Should().Be("false");
        cachedValue.Should().Be("false");
        refreshedValue.Should().Be("true");
    }

    [TestMethod]
    public async Task GetValueAsyncOfT_ConvertsPrimitiveValues()
    {
        await using var db = CreateDbContext(nameof(GetValueAsyncOfT_ConvertsPrimitiveValues));
        db.SiteConfiguration.AddRange(
            new SiteConfiguration
            {
                Key = "feature:enabled",
                Value = "true",
                UpdatedAt = DateTime.UtcNow
            },
            new SiteConfiguration
            {
                Key = "limits:max-items",
                Value = "25",
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        using var cache = CreateCache();
        var service = CreateService(db, cache);

        var enabled = await service.GetValueAsync<bool>("feature:enabled");
        var maxItems = await service.GetValueAsync<int>("limits:max-items");

        enabled.Should().BeTrue();
        maxItems.Should().Be(25);
    }
}
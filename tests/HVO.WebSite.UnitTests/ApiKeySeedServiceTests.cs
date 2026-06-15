using FluentAssertions;
using HVO.DataModels.Data;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Middleware;
using HVO.WebSite.v9.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class ApiKeySeedServiceTests
{
    [TestMethod]
    public async Task StartAsync_SeedsWeatherReadApiKey_WhenConfigured()
    {
        var dbName = $"{nameof(StartAsync_SeedsWeatherReadApiKey_WhenConfigured)}-{Guid.NewGuid():N}";
        var services = new ServiceCollection()
            .AddDbContext<HvoV9DbContext>(options => options.UseInMemoryDatabase(dbName))
            .BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:WeatherReadApiKey"] = "weather-read-test-key",
            })
            .Build();

        var seeder = new ApiKeySeedService(
            services,
            configuration,
            NullLogger<ApiKeySeedService>.Instance);

        await seeder.StartAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        var keyHash = ApiKeyAuthMiddleware.HashKey("weather-read-test-key");

        var apiKey = await db.ApiKeys
            .Include(k => k.Claims)
            .SingleAsync(k => k.KeyHash == keyHash);

        apiKey.Name.Should().Be("Weather API — read");
        apiKey.IsActive.Should().BeTrue();
        apiKey.Claims.Should().ContainSingle(c =>
            c.ClaimType == "scope" && c.ClaimValue == ApiScopes.WeatherRead);
    }

    [TestMethod]
    public async Task StartAsync_SeedsPowerReadApiKey_WhenConfigured()
    {
        var dbName = $"{nameof(StartAsync_SeedsPowerReadApiKey_WhenConfigured)}-{Guid.NewGuid():N}";
        var services = new ServiceCollection()
            .AddDbContext<HvoV9DbContext>(options => options.UseInMemoryDatabase(dbName))
            .BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:PowerReadApiKey"] = "power-read-test-key",
            })
            .Build();

        var seeder = new ApiKeySeedService(
            services,
            configuration,
            NullLogger<ApiKeySeedService>.Instance);

        await seeder.StartAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        var keyHash = ApiKeyAuthMiddleware.HashKey("power-read-test-key");

        var apiKey = await db.ApiKeys
            .Include(k => k.Claims)
            .SingleAsync(k => k.KeyHash == keyHash);

        apiKey.Name.Should().Be("Power API — read");
        apiKey.IsActive.Should().BeTrue();
        apiKey.Claims.Should().ContainSingle(c =>
            c.ClaimType == "scope" && c.ClaimValue == ApiScopes.PowerRead);
    }

    [TestMethod]
    public async Task StartAsync_DoesNotDuplicatePowerReadApiKey_WhenRunTwice()
    {
        var dbName = $"{nameof(StartAsync_DoesNotDuplicatePowerReadApiKey_WhenRunTwice)}-{Guid.NewGuid():N}";
        var services = new ServiceCollection()
            .AddDbContext<HvoV9DbContext>(options => options.UseInMemoryDatabase(dbName))
            .BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:PowerReadApiKey"] = "power-read-test-key",
            })
            .Build();

        var seeder = new ApiKeySeedService(
            services,
            configuration,
            NullLogger<ApiKeySeedService>.Instance);

        await seeder.StartAsync(CancellationToken.None);
        await seeder.StartAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        var keyHash = ApiKeyAuthMiddleware.HashKey("power-read-test-key");

        var seededCount = await db.ApiKeys.CountAsync(k => k.KeyHash == keyHash);

        seededCount.Should().Be(1);
    }
}

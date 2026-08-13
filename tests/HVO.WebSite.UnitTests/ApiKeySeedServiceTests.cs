using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
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
    public async Task StartAsync_SeedsDedicatedSmartShuntOwnerWithExactSourceClaim()
    {
        var database = $"seed-smartshunt-{Guid.NewGuid():N}";
        var services = new ServiceCollection().AddDbContext<HvoV9DbContext>(options => options.UseInMemoryDatabase(database)).BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seeding:SmartShuntApiKey"] = "smartshunt-key",
            ["Seeding:SmartShuntSourceId"] = "smartshunt-main"
        }).Build();
        await new ApiKeySeedService(services, configuration, NullLogger<ApiKeySeedService>.Instance).StartAsync(CancellationToken.None);
        await using var scope = services.CreateAsyncScope();
        var key = await scope.ServiceProvider.GetRequiredService<HvoV9DbContext>().ApiKeys.Include(item => item.Claims).SingleAsync();
        key.Claims.Should().Contain(item => item.ClaimType == "scope" && item.ClaimValue == ApiScopes.PowerIngest);
        key.Claims.Should().Contain(item => item.ClaimType == "source" && item.ClaimValue == "smartshunt-main");
    }

    [TestMethod]
    public async Task StartAsync_ReplacesStaleSmartShuntSourceReservation()
    {
        var database = $"seed-smartshunt-rotation-{Guid.NewGuid():N}";
        var services = new ServiceCollection().AddDbContext<HvoV9DbContext>(options => options.UseInMemoryDatabase(database)).BuildServiceProvider();
        var first = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seeding:SmartShuntApiKey"] = "smartshunt-key",
            ["Seeding:SmartShuntSourceId"] = "smartshunt-old"
        }).Build();
        await new ApiKeySeedService(services, first, NullLogger<ApiKeySeedService>.Instance).StartAsync(CancellationToken.None);
        var second = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seeding:SmartShuntApiKey"] = "smartshunt-key",
            ["Seeding:SmartShuntSourceId"] = "smartshunt-main"
        }).Build();

        await new ApiKeySeedService(services, second, NullLogger<ApiKeySeedService>.Instance).StartAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var key = await scope.ServiceProvider.GetRequiredService<HvoV9DbContext>().ApiKeys.Include(item => item.Claims).SingleAsync();
        key.Claims.Where(item => item.ClaimType == "source").Select(item => item.ClaimValue)
            .Should().Equal("smartshunt-main");
    }

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

    [TestMethod]
    public async Task StartAsync_TrimsConfiguredApiKeyBeforeHashing()
    {
        var dbName = $"{nameof(StartAsync_TrimsConfiguredApiKeyBeforeHashing)}-{Guid.NewGuid():N}";
        var services = new ServiceCollection()
            .AddDbContext<HvoV9DbContext>(options => options.UseInMemoryDatabase(dbName))
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:BmsApiKey"] = "\r\nbms-test-key\n",
            })
            .Build();

        await new ApiKeySeedService(
            services,
            configuration,
            NullLogger<ApiKeySeedService>.Instance).StartAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        var keyHash = ApiKeyAuthMiddleware.HashKey("bms-test-key");

        (await db.ApiKeys.CountAsync(key => key.KeyHash == keyHash)).Should().Be(1);
    }

    [TestMethod]
    public async Task StartAsync_SeedsHomeAssistantExporterScopesAndSourceClaimsIdempotently()
    {
        var dbName = $"{nameof(StartAsync_SeedsHomeAssistantExporterScopesAndSourceClaimsIdempotently)}-{Guid.NewGuid():N}";
        var services = new ServiceCollection()
            .AddDbContext<HvoV9DbContext>(options => options.UseInMemoryDatabase(dbName))
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:HomeAssistantExporterApiKey"] = "ha-exporter-test-key",
                ["Seeding:HomeAssistantExporterSources:0"] = "kasa:plug-1",
                ["Seeding:HomeAssistantExporterSources:1"] = "govee:sensor-1"
            })
            .Build();
        await using (var setupScope = services.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
            var expiredKey = new ApiKey
            {
                Id = Guid.NewGuid(),
                KeyHash = new string('e', 64),
                Name = "Expired source owner",
                Type = ApiKeyType.System,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            };
            expiredKey.Claims.Add(new ApiKeyClaim
            {
                ApiKeyId = expiredKey.Id,
                ClaimType = "source",
                ClaimValue = "kasa:plug-1"
            });
            setupDb.ApiKeys.Add(expiredKey);
            await setupDb.SaveChangesAsync();
        }
        var seeder = new ApiKeySeedService(services, configuration, NullLogger<ApiKeySeedService>.Instance);

        await seeder.StartAsync(CancellationToken.None);
        configuration["Seeding:HomeAssistantExporterSources:1"] = "govee:sensor-2";
        await seeder.StartAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        var keyHash = ApiKeyAuthMiddleware.HashKey("ha-exporter-test-key");
        var apiKey = await db.ApiKeys.Include(key => key.Claims).SingleAsync(key => key.KeyHash == keyHash);
        apiKey.Claims.Should().HaveCount(4);
        apiKey.Claims.Should().Contain(claim => claim.ClaimType == "scope" && claim.ClaimValue == ApiScopes.PowerIngest);
        apiKey.Claims.Should().Contain(claim => claim.ClaimType == "scope" && claim.ClaimValue == ApiScopes.WeatherIngest);
        apiKey.Claims.Should().Contain(claim => claim.ClaimType == "source" && claim.ClaimValue == "kasa:plug-1");
        apiKey.Claims.Should().Contain(claim => claim.ClaimType == "source" && claim.ClaimValue == "govee:sensor-2");
        apiKey.Claims.Should().NotContain(claim => claim.ClaimType == "source" && claim.ClaimValue == "govee:sensor-1");
    }
}

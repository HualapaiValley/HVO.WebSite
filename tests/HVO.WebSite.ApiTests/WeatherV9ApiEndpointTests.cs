using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Middleware;
using HVO.WebSite.v9.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;

namespace HVO.WebSite.ApiTests;

/// <summary>
/// Integration tests for the WeatherV9 endpoints, exercising the full middleware
/// pipeline including API key authentication.
/// </summary>
[TestClass]
public sealed class WeatherV9ApiEndpointTests
{
    // Keys created once for the test class
    private const string IngestPlaintext = "test-ingest-key-abc123";
    private const string ReadPlaintext = "test-read-key-xyz789";
    private const string InvalidPlaintext = "totally-invalid-key";

    private static WeatherV9TestFactory _factory = null!;
    private HttpClient _client = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new WeatherV9TestFactory();

        // Seed the in-memory DB with two API keys — ingest scope and read scope
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        await db.Database.EnsureCreatedAsync();

        db.ApiKeys.AddRange(
            new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = "Ingest Test Key",
                KeyHash = ApiKeyAuthMiddleware.HashKey(IngestPlaintext),
                Type = ApiKeyType.System,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = "ingest:weather" }]
            },
            new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = "Read Test Key",
                KeyHash = ApiKeyAuthMiddleware.HashKey(ReadPlaintext),
                Type = ApiKeyType.System,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = "read:weather" }]
            });

        await db.SaveChangesAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _factory.Dispose();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        // AllowAutoRedirect=false prevents OIDC 302 redirects from hiding 401/403 responses
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _client.Dispose();
    }

    // -------------------------------------------------------------------------
    // POST /api/v1/weather/raw  (requires ingest:weather)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task IngestRaw_Returns401_WhenApiKeyIsInvalid()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", InvalidPlaintext);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/weather/raw",
            ValidIngestPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task IngestRaw_Returns403_WhenKeyHasReadScopeOnly()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/weather/raw",
            ValidIngestPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task IngestRaw_Returns201_WhenKeyHasIngestScope()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/weather/raw",
            ValidIngestPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<WeatherRawResponse>();
        body.Should().NotBeNull();
        body!.StationId.Should().Be("test-station");
        body.TemperatureF.Should().Be(72.5);
        body.Id.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/weather/raw/recent  (requires read:weather)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetRecentRaw_Returns401_WhenApiKeyIsInvalid()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", InvalidPlaintext);

        var response = await _client.GetAsync("/api/v1/weather/raw/recent");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task GetRecentRaw_Returns403_WhenKeyHasIngestScopeOnly()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var response = await _client.GetAsync("/api/v1/weather/raw/recent");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task GetRecentRaw_Returns200_WhenKeyHasReadScope()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);

        var response = await _client.GetAsync("/api/v1/weather/raw/recent?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<WeatherRawResponse>>();
        body.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/weather/hourly/recent  (requires read:weather)
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetRecentHourly_Returns200_WhenKeyHasReadScope()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);

        var response = await _client.GetAsync("/api/v1/weather/hourly/recent?limit=24");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<WeatherHourlyResponse>>();
        body.Should().NotBeNull();
    }

    [TestMethod]
    public async Task GetRecentHourly_Returns403_WhenKeyHasIngestScopeOnly()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var response = await _client.GetAsync("/api/v1/weather/hourly/recent");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IngestWeatherRawRequest ValidIngestPayload() => new()
    {
        StationId = "test-station",
        RecordedAt = DateTime.UtcNow,
        TemperatureF = 72.5,
        HumidityPercent = 38.0,
        BarometricPressureInHg = 29.92,
        WindSpeedMph = 6.0,
        WindDirectionDegrees = 270
    };

    // -------------------------------------------------------------------------
    // Test factory
    // -------------------------------------------------------------------------

    private sealed class WeatherV9TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // Override config to prevent Key Vault and Azure SQL connections at test startup
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Disable Key Vault loading
                    ["KeyVault:Uri"] = string.Empty,
                    // Dummy AzureAd values (OIDC not exercised in API key tests)
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000001",
                    ["AzureAd:ClientSecret"] = "test-dummy-secret",
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000002",
                    // Dummy connection string (never used — DbContexts replaced below)
                    ["ConnectionStrings:HualapaiValleyObservatory"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=_WeatherV9Test;Trusted_Connection=True;"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Replace SQL Server DbContexts with InMemory equivalents.
                // Use unique DB names so tests in this class share a single in-memory store.
                ReplaceWithInMemory<HvoV9DbContext>(services, "v9-apitest");
                ReplaceWithInMemory<HvoDbContext>(services, "legacy-apitest");
            });
        }

        private static void ReplaceWithInMemory<TContext>(IServiceCollection services, string dbName)
            where TContext : DbContext
        {
            // Remove all registered option configurations for this context type
            services.RemoveAll(typeof(DbContextOptions<TContext>));

            // Also sweep up any IDbContextOptionsConfiguration<TContext> service descriptors
            // (EF Core 8+ internal mechanism for storing per-context options)
            var toRemove = services
                .Where(d =>
                    d.ServiceType.IsGenericType &&
                    d.ServiceType.GetGenericArguments().Length == 1 &&
                    d.ServiceType.GetGenericArguments()[0] == typeof(TContext) &&
                    d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<TContext>(opt => opt.UseInMemoryDatabase(dbName));
        }
    }
}

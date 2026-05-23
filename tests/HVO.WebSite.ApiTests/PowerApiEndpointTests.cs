using System.Net;
using System.Net.Http.Json;
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

namespace HVO.WebSite.ApiTests;

[TestClass]
public sealed class PowerApiEndpointTests
{
    private const string IngestPlaintext = "test-power-ingest-key-abc123";
    private const string ReadPlaintext = "test-power-read-key-xyz789";
    private const string ApiReadPlaintext = "test-api-read-key-xyz789";
    private const string InvalidPlaintext = "totally-invalid-power-key";

    private static PowerApiTestFactory _factory = null!;
    private HttpClient _client = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new PowerApiTestFactory();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        await db.Database.EnsureCreatedAsync();

        db.ApiKeys.AddRange(
            new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = "Power Ingest Test Key",
                KeyHash = ApiKeyAuthMiddleware.HashKey(IngestPlaintext),
                Type = ApiKeyType.System,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = ApiScopes.PowerIngest }]
            },
            new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = "Power Read Test Key",
                KeyHash = ApiKeyAuthMiddleware.HashKey(ReadPlaintext),
                Type = ApiKeyType.System,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = ApiScopes.PowerRead }]
            },
            new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = "API Read Test Key",
                KeyHash = ApiKeyAuthMiddleware.HashKey(ApiReadPlaintext),
                Type = ApiKeyType.System,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = ApiScopes.ApiRead }]
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

    [TestMethod]
    public async Task IngestReadings_Returns401_WhenApiKeyIsInvalid()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", InvalidPlaintext);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/power/readings",
            new[] { ValidPayload(Guid.NewGuid().ToString("N")) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task IngestReadings_Returns403_WhenKeyHasReadScopeOnly()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/power/readings",
            new[] { ValidPayload(Guid.NewGuid().ToString("N")) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task IngestReadings_PersistsAndIsIdempotent_WhenKeyHasIngestScope()
    {
        var sourceId = $"power-api-test-{Guid.NewGuid():N}";
        var payload = new[] { ValidPayload(sourceId, "2026-05-23T08:00:00Z") };
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var firstResponse = await _client.PostAsJsonAsync("/api/v1/power/readings", payload);
        var retryResponse = await _client.PostAsJsonAsync("/api/v1/power/readings", payload);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<PowerReadingBatchResponse>();
        firstBody.Should().NotBeNull();
        firstBody!.Inserted.Should().Be(1);
        firstBody.Skipped.Should().Be(0);
        firstBody.Failed.Should().BeEmpty();

        retryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var retryBody = await retryResponse.Content.ReadFromJsonAsync<PowerReadingBatchResponse>();
        retryBody.Should().NotBeNull();
        retryBody!.Inserted.Should().Be(0);
        retryBody.Skipped.Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        db.PowerReadings.Count(r => r.SourceId == sourceId).Should().Be(1);
    }

    [TestMethod]
    public async Task IngestReadings_DeadLettersInvalidRecordsAndPersistsValidRecords()
    {
        var sourceId = $"power-api-test-{Guid.NewGuid():N}";
        var payload = new[]
        {
            ValidPayload(sourceId, "2026-05-23T08:10:00Z"),
            ValidPayload($"{sourceId}-bad", "2026-05-23T08:10:10Z", batteryStateOfChargePercent: 150),
        };
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var response = await _client.PostAsJsonAsync("/api/v1/power/readings", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<PowerReadingBatchResponse>();
        body.Should().NotBeNull();
        body!.Inserted.Should().Be(1);
        body.Failed.Should().HaveCount(1);
        body.Failed[0].Error.Should().Contain("BatteryStateOfChargePercent");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        db.PowerReadings.Count(r => r.SourceId == sourceId).Should().Be(1);
        db.PowerReadings.Count(r => r.SourceId == $"{sourceId}-bad").Should().Be(0);
    }

    [TestMethod]
    public async Task GetRecentReadings_Returns403_WhenKeyHasIngestScopeOnly()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var response = await _client.GetAsync("/api/v1/power/readings/recent");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task GetRecentReadings_Returns200_WhenKeyHasPowerReadScope()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);

        var response = await _client.GetAsync("/api/v1/power/readings/recent?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PowerReadingResponse>>();
        body.Should().NotBeNull();
    }

    [TestMethod]
    public async Task GetRecentReadings_Returns200_WhenKeyHasApiReadScope()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", ApiReadPlaintext);

        var response = await _client.GetAsync("/api/v1/power/readings/recent?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PowerReadingResponse>>();
        body.Should().NotBeNull();
    }

    private static PowerReadingIngestRequest ValidPayload(
        string sourceId,
        string recordedAt = "2026-05-23T07:00:00Z",
        double? batteryStateOfChargePercent = 82) => new()
    {
        SourceId = sourceId,
        SourceSystem = "solarassistant",
        DeviceId = "total",
        RecordedAtUtc = DateTime.Parse(recordedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
        PvPowerW = 1200,
        LoadPowerW = 900,
        GridPowerW = -50,
        BatteryPowerW = -250,
        BatteryStateOfChargePercent = batteryStateOfChargePercent,
        BatteryVoltageV = 53.2,
        BatteryCurrentA = -4.7,
        GridVoltageV = 240,
        GridFrequencyHz = 60,
        OutputVoltageV = 120,
        OutputFrequencyHz = 60,
        LoadPercentage = 23,
    };

    private sealed class PowerApiTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KeyVault:Uri"] = string.Empty,
                    ["ApplicationInsights:ConnectionString"] = string.Empty,
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000001",
                    ["AzureAd:ClientSecret"] = "test-dummy-secret",
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000002",
                    ["ConnectionStrings:HualapaiValleyObservatory"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=_PowerApiTest;Trusted_Connection=True;"
                });
            });

            builder.ConfigureServices(services =>
            {
                ReplaceWithInMemory<HvoV9DbContext>(services, "v9-power-apitest");
                ReplaceWithInMemory<HvoDbContext>(services, "legacy-power-apitest");
            });
        }

        private static void ReplaceWithInMemory<TContext>(IServiceCollection services, string dbName)
            where TContext : DbContext
        {
            services.RemoveAll(typeof(DbContextOptions<TContext>));

            var toRemove = services
                .Where(d =>
                    d.ServiceType.IsGenericType &&
                    d.ServiceType.GetGenericArguments().Length == 1 &&
                    d.ServiceType.GetGenericArguments()[0] == typeof(TContext) &&
                    d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal))
                .ToList();

            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<TContext>(opt => opt.UseInMemoryDatabase(dbName));
        }
    }
}

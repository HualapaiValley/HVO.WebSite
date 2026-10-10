using FluentAssertions;
using HVO.Core.Results;
using HVO.DataModels.Data;
using HVO.DataModels.Models;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.Weather;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Middleware;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HVO.WebSite.ApiTests;

[TestClass]
public sealed class WeatherApiEndpointsTests
{
    private const string ReadPlaintext = "weather-read-endpoints-key";
    private const string ApiReadPlaintext = "weather-api-read-endpoints-key";
    private const string IngestPlaintext = "weather-ingest-endpoints-key";
    private const string InvalidPlaintext = "weather-invalid-endpoints-key";

    [TestMethod]
    public async Task ReadEndpoints_Return401_WhenApiKeyIsMissing()
    {
        using var factory = new TestWebApplicationFactory();
        await factory.SeedApiKeysAsync();
        using var client = CreateClient(factory);

        foreach (var path in WeatherReadPaths())
        {
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }
    }

    [TestMethod]
    public async Task ReadEndpoints_Return401_WhenApiKeyIsInvalid()
    {
        using var factory = new TestWebApplicationFactory();
        await factory.SeedApiKeysAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", InvalidPlaintext);

        foreach (var path in WeatherReadPaths().Append("/api/v1/weather/current"))
        {
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }
    }

    [TestMethod]
    public async Task ReadEndpoints_Return403_WhenApiKeyHasIngestScopeOnly()
    {
        using var factory = new TestWebApplicationFactory();
        await factory.SeedApiKeysAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        foreach (var path in WeatherReadPaths())
        {
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }

        var publicResponse = await client.GetAsync("/api/v1/weather/current");
        publicResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task ReadEndpoints_Return200_WhenApiKeyHasReadScope()
    {
        using var factory = new TestWebApplicationFactory();
        await factory.SeedApiKeysAsync();
        using var client = CreateClient(factory);

        foreach (var apiKey in new[] { ReadPlaintext, ApiReadPlaintext })
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

            foreach (var path in WeatherReadPaths().Append("/api/v1/weather/current"))
            {
                var response = await client.GetAsync(path);

                response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} with key {apiKey}");
            }
        }
    }

    [TestMethod]
    public async Task LatestEndpoint_ReturnsExpectedPayload_WhenApiKeyHasWeatherReadScope()
    {
        using var factory = new TestWebApplicationFactory();
        await factory.SeedApiKeysAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);

        var response = await client.GetAsync("/api/v1/weather/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<LatestWeatherResponse>();
        payload.Should().NotBeNull();
        payload!.MachineName.Should().Be("api-test-host");
        payload.Data.Should().NotBeNull();
    }

    [TestMethod]
    public async Task CurrentEndpoint_ReturnsLiveIngestedWeather_WithoutApiKey()
    {
        using var factory = new TestWebApplicationFactory(useRealWeatherService: true);
        await factory.SeedApiKeysAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);
        var recordedAt = new DateTime(2026, 10, 10, 21, 0, 0, DateTimeKind.Utc);

        var ingest = await client.PostAsJsonAsync("/api/v1/weather/raw", new IngestWeatherRawRequest
        {
            StationId = "hvo-davis-01", RecordedAt = recordedAt,
            TemperatureF = 84.7, HumidityPercent = 26, WindSpeedMph = 15
        });
        ingest.StatusCode.Should().Be(HttpStatusCode.Created);
        client.DefaultRequestHeaders.Remove("X-Api-Key");

        var response = await client.GetAsync("/api/v1/weather/current");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<CurrentWeatherResponse>();
        payload.Should().NotBeNull();
        payload!.Current.RecordDateTime.Should().Be(new DateTimeOffset(recordedAt));
        payload.Current.OutsideTemperature.Should().Be(84.7m);
        payload.Current.OutsideHumidity.Should().Be(26);
        payload.Current.WindSpeed.Should().Be(15);
        payload.TodaysExtremes!.OutsideTemperature.High.Should().Be(84.7m);
        payload.TodaysExtremes.OutsideTemperature.Low.Should().Be(84.7m);

        var anonymousIngest = await client.PostAsJsonAsync("/api/v1/weather/raw", new IngestWeatherRawRequest
        {
            StationId = "hvo-davis-01", RecordedAt = recordedAt.AddMinutes(1), TemperatureF = 90
        });
        anonymousIngest.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task CurrentEndpoint_Returns404WithoutApiKey_WhenNoObservationsExist()
    {
        using var factory = new TestWebApplicationFactory(useRealWeatherService: true);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/v1/weather/current");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task OpenApiEndpoint_DescribesVersionedApi()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        document.RootElement.GetProperty("info").GetProperty("version").GetString().Should().Be("v1.0");
        document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/weather/latest", out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task ArchiveBatch_ReturnsPerRecordFailuresInsteadOfApiControllerShortCircuit()
    {
        using var factory = new TestWebApplicationFactory();
        await factory.SeedApiKeysAsync();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);
        var at = new DateTime(2026, 8, 12, 1, 0, 0, DateTimeKind.Utc);
        var valid = new DavisWeatherArchivePayload
        {
            StationId = "hvo-davis-01",
            RecordedAtUtc = at,
            ConsoleRecordedAtLocal = DateTime.SpecifyKind(at.AddHours(-7), DateTimeKind.Unspecified),
            ArchiveIntervalMinutes = 5,
        };
        var invalid = valid with { RecordedAtUtc = at.AddMinutes(5), ArchiveIntervalMinutes = 0 };

        var response = await client.PostAsJsonAsync("/api/v1/weather/archive/batch", new[] { valid, invalid });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<WeatherArchiveBatchResponse>();
        result.Should().NotBeNull();
        result!.Inserted.Should().Be(1);
        result.Failed.Should().ContainSingle();
    }

    private static HttpClient CreateClient(TestWebApplicationFactory factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    private static string[] WeatherReadPaths() =>
    [
        "/api/v1/weather/latest",
        "/api/v1/weather/highs-lows",
        "/api/v1/weather/raw/recent",
        "/api/v1/weather/hourly/recent"
    ];

    private sealed class TestWebApplicationFactory(bool useRealWeatherService = false) : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"weather-read-endpoints-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HualapaiValleyObservatory"] = "Server=localhost;Database=HvoTest;User Id=sa;Password=Password!123;TrustServerCertificate=True;"
                });
            });

            builder.ConfigureServices(services =>
            {
                // ApiKeySeedService attempts database migrations at startup;
                // remove it so this test factory does not need a real SQL Server.
                var seedDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType == typeof(ApiKeySeedService));
                if (seedDescriptor is not null)
                    services.Remove(seedDescriptor);

                if (useRealWeatherService)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                }
                else
                {
                    services.RemoveAll<IWeatherService>();
                    services.AddScoped<IWeatherService, FakeWeatherService>();
                }

                ReplaceWithInMemory<HvoV9DbContext>(services, _databaseName);
            });
        }

        public async Task SeedApiKeysAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
            await db.Database.EnsureCreatedAsync();

            db.ApiKeys.AddRange(
                new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = "API Read Endpoint Test Key",
                    KeyHash = ApiKeyAuthMiddleware.HashKey(ApiReadPlaintext),
                    Type = ApiKeyType.System,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = ApiScopes.ApiRead }]
                },
                new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = "Weather Read Endpoint Test Key",
                    KeyHash = ApiKeyAuthMiddleware.HashKey(ReadPlaintext),
                    Type = ApiKeyType.System,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = ApiScopes.WeatherRead }]
                },
                new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = "Weather Ingest Endpoint Test Key",
                    KeyHash = ApiKeyAuthMiddleware.HashKey(IngestPlaintext),
                    Type = ApiKeyType.System,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = ApiScopes.WeatherIngest }]
                });

            await db.SaveChangesAsync();
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
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<TContext>(opt => opt.UseInMemoryDatabase(dbName));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 10, 21, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeWeatherService : IWeatherService
    {
        public Task<Result<LatestWeatherResponse>> GetLatestWeatherRecordAsync()
        {
            var latestRecord = new DavisVantageProConsoleRecordsNew
            {
                Id = 1,
                RecordDateTime = DateTimeOffset.UtcNow,
                OutsideTemperature = 72.5m,
                OutsideHumidity = 27,
                WindSpeed = 8,
                WindDirection = 180
            };

            var response = new LatestWeatherResponse
            {
                Timestamp = DateTime.UtcNow,
                MachineName = "api-test-host",
                Data = latestRecord
            };

            return Task.FromResult(Result<LatestWeatherResponse>.Success(response));
        }

        public Task<Result<WeatherHighsLowsResponse>> GetWeatherHighsLowsAsync(DateTimeOffset? startDate, DateTimeOffset? endDate)
            => Task.FromResult(Result<WeatherHighsLowsResponse>.Success(new WeatherHighsLowsResponse()));

        public Task<Result<CurrentWeatherResponse>> GetCurrentWeatherConditionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<CurrentWeatherResponse>.Success(new CurrentWeatherResponse()));
    }
}

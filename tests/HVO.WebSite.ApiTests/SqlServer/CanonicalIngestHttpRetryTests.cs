using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Middleware;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass]
[TestCategory("Integration")]
[TestCategory("SqlServerIntegration")]
[DoNotParallelize]
public sealed class CanonicalIngestHttpRetryTests
{
    private const string Device = "00:11:22:33:44:55";
    private const string Key = "isolated-canonical-ingest-http-key";
    private static readonly DateTime At = new(2026, 8, 12, 17, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow("power", false)]
    [DataRow("weather", false)]
    [DataRow("bms", false)]
    [DataRow("power", true)]
    [DataRow("weather", true)]
    public async Task PartialOverlap_HttpDoesNotRetireMissingRecordsAndRetryAccountsForEveryAlias(string kind, bool ownedSource)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        int deviceId;
        await using (var seed = database.CreateContext())
        {
            var claims = new List<ApiKeyClaim>
            {
                new() { ClaimType = "scope", ClaimValue = ApiScopes.PowerIngest },
                new() { ClaimType = "scope", ClaimValue = ApiScopes.WeatherIngest },
                new() { ClaimType = "scope", ClaimValue = ApiScopes.BmsIngest }
            };
            if (ownedSource)
                claims.Add(new ApiKeyClaim { ClaimType = "source", ClaimValue = kind == "weather" ? "http-weather" : "http-power" });
            seed.ApiKeys.Add(new ApiKey
            {
                Id = Guid.NewGuid(), Name = "isolated-ingest", KeyHash = ApiKeyAuthMiddleware.HashKey(Key),
                Type = ApiKeyType.System, IsActive = true, CreatedAt = At,
                Claims = claims
            });
            var device = new BmsDevice { Address = Device, Alias = "test", FirstSeenAt = At };
            seed.BmsDevices.Add(device);
            await seed.SaveChangesAsync();
            deviceId = device.Id;
        }
        var race = new BeforeFirstSaveInterceptor(async ct =>
        {
            await using var competing = database.CreateContext();
            if (kind == "power")
                competing.PowerReadings.Add(new PowerReading { SourceId = "http-power", RecordedAt = At });
            else if (kind == "weather")
                competing.WeatherRaw.Add(new WeatherRaw { StationId = "http-weather", RecordedAt = At });
            else
                competing.BmsReadings.Add(new BmsReading
                {
                    DeviceId = deviceId, RecordedAt = At,
                    CellVoltages = [new BmsCellVoltage { CellIndex = 1, VoltageMv = 3250 }]
                });
            await competing.SaveChangesAsync(ct);
        });
        await using var application = new SqlIngestApplication(database, race);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Api-Key", Key);
        var path = kind switch
        {
            "power" => "/api/v1/power/readings",
            "weather" => "/api/v1/weather/raw/batch",
            _ => "/api/v1/bms/readings"
        };
        object payload = kind switch
        {
            "power" => new PowerReadingPayload[]
            {
                new() { SourceId = "http-power", RecordedAtUtc = At },
                new() { SourceId = "http-power", RecordedAtUtc = At.AddMinutes(1) },
                new() { SourceId = "http-power", RecordedAtUtc = At.AddMinutes(1) }
            },
            "weather" => new IngestWeatherRawRequest[]
            {
                new() { StationId = "http-weather", RecordedAt = At },
                new() { StationId = "http-weather", RecordedAt = At.AddMinutes(1) },
                new() { StationId = " http-weather ", RecordedAt = At.AddMinutes(1) }
            },
            _ => new[] { Request(0), Request(1), Request(1) }
        };
        var response = await client.PostAsJsonAsync(path, payload);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("Retry is safe").And.NotContain("SqlException").And.NotContain(Key);
        await using (var verified = database.CreateContext())
        {
            var count = kind switch
            {
                "power" => await verified.PowerReadings.CountAsync(),
                "weather" => await verified.WeatherRaw.CountAsync(),
                _ => await verified.BmsReadings.CountAsync()
            };
            count.Should().Be(1);
        }
        var retry = await client.PostAsJsonAsync(path, payload);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = System.Text.Json.JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("inserted").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("skipped").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("failed").GetArrayLength().Should().Be(0);
        var replay = await client.PostAsJsonAsync(path, payload);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        using var replayJson = System.Text.Json.JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        replayJson.RootElement.GetProperty("inserted").GetInt32().Should().Be(0);
        replayJson.RootElement.GetProperty("skipped").GetInt32().Should().Be(3);
    }

    private static BmsIngestRequest Request(int minute) => new()
    {
        Reading = new BmsReadingRequest
        {
            DeviceAddress = Device, DeviceAlias = "test", RecordedAtUtc = At.AddMinutes(minute), CellVoltagesMv = [3250]
        }
    };

    private sealed class SqlIngestApplication(SqlServerDatabase database, params IInterceptor[] interceptors) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVault:Uri"] = string.Empty,
                ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000001",
                ["AzureAd:ClientSecret"] = "isolated-test-dummy",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000002",
                ["ConnectionStrings:HualapaiValleyObservatory"] = "Server=127.0.0.1;Database=Unused;User Id=unused;Password=unused;"
            }));
            builder.ConfigureServices(services =>
            {
                var seed = services.FirstOrDefault(d => d.ImplementationType == typeof(ApiKeySeedService));
                if (seed is not null) services.Remove(seed);
                services.RemoveAll<HvoV9DbContext>();
                services.AddScoped(_ => database.CreateContext(interceptors));
            });
        }
    }
}

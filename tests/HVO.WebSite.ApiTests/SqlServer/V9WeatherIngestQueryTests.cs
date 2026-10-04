using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.Weather;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Controllers;
using HVO.WebSite.v9.Middleware;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass]
[TestCategory("Integration")]
[TestCategory("SqlServerIntegration")]
[DoNotParallelize]
public sealed class V9WeatherIngestQueryTests
{
    private const string Key = "test-owned-v9-weather-ingest";
    private static readonly DateTime At = new(2026, 8, 12, 17, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow(null)]
    [DataRow("20260528073744_AddGatewayStatusSnapshots")]
    [DataRow("20260812031452_AddSmartShuntDetailSnapshots")]
    public async Task DavisIngest_NewWebsiteQueriesObserveCanonicalRawAndArchiveAfterBootstrapOrUpgrade(string? priorMigration)
    {
        await using var database = await SqlServerDatabase.CreateAsync(priorMigration);
        await using (var db = database.CreateContext())
        {
            // Exercise the unchanged actual migration chain, including creation of WeatherArchive.
            await db.Database.MigrateAsync();
            db.ApiKeys.Add(new ApiKey
            {
                Name = "Test-owned canonical Davis writer", KeyHash = ApiKeyAuthMiddleware.HashKey(Key),
                Type = ApiKeyType.System, IsActive = true,
                Claims = [new ApiKeyClaim { ClaimType = "scope", ClaimValue = ApiScopes.WeatherIngest }]
            });
            await db.SaveChangesAsync();
        }
        var clock = new ManualClock { Now = new DateTimeOffset(At.AddMinutes(2)) };
        using var application = new WeatherApplication(database, clock);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", Key);
        var raw = new[]
        {
            Live("davis-test", At.AddMinutes(2), 72), Live("davis-test", At, 70),
            Live("other-station", At.AddMinutes(2), 99), Live("davis-test", At.AddMinutes(3), 88),
            Live("davis-test", At.AddMinutes(1), null)
        };
        using var ingested = await client.PostAsJsonAsync("/api/v1/weather/raw/batch", raw);
        ingested.StatusCode.Should().Be(HttpStatusCode.Created);
        var rawResult = await ingested.Content.ReadFromJsonAsync<WeatherRawBatchResponse>();
        rawResult!.Inserted.Should().Be(5);
        rawResult.Failed.Should().BeEmpty();
        using var replay = await client.PostAsJsonAsync("/api/v1/weather/raw/batch", raw);
        (await replay.Content.ReadFromJsonAsync<WeatherRawBatchResponse>())!.Skipped.Should().Be(5);
        var archive = raw.Select(payload => new DavisWeatherArchivePayload
        {
            StationId = payload.StationId, RecordedAtUtc = payload.RecordedAtUtc,
            ConsoleRecordedAtLocal = DateTime.SpecifyKind(payload.RecordedAtUtc.AddHours(-7), DateTimeKind.Unspecified),
            ArchiveIntervalMinutes = 5, TemperatureF = payload.TemperatureF,
            HighTemperatureF = 75, LowTemperatureF = 65, RainfallInches = .1, RainRateInchesPerHour = .2
        }).ToArray();
        using var archiveIngested = await client.PostAsJsonAsync("/api/v1/weather/archive/batch", archive);
        archiveIngested.StatusCode.Should().Be(HttpStatusCode.Created);
        (await archiveIngested.Content.ReadFromJsonAsync<WeatherArchiveBatchResponse>())!.Inserted.Should().Be(5);

        using var scope = application.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IV9WeatherQueryService>();
        scope.ServiceProvider.GetRequiredService<IWeatherService>().Should().BeOfType<WeatherService>();
        var current = await queries.GetCurrentAsync("davis-test");
        current.Availability.Should().Be(V9WeatherAvailability.Current);
        current.Observation!.TemperatureF.Should().Be(72);
        current.Observation.RecordedAtUtc.Should().Be(At.AddMinutes(2));
        current.Observation.RecordedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        (await queries.GetLatestAsync("other-station"))!.TemperatureF.Should().Be(99);
        (await queries.GetCurrentAsync("missing")).Availability.Should().Be(V9WeatherAvailability.NoData);
        var start = new DateTimeOffset(At).ToOffset(TimeSpan.FromHours(-7));
        var end = start.AddMinutes(4);
        var first = await queries.GetRawHistoryAsync("davis-test", start, end, 2);
        first.Records.Select(row => row.RecordedAtUtc).Should().Equal(At, At.AddMinutes(1));
        first.Records[1].TemperatureF.Should().BeNull();
        first.HasMore.Should().BeTrue();
        var second = await queries.GetRawHistoryAsync("davis-test", start, end, 2, first.NextAfterUtc);
        second.Records.Should().ContainSingle().Which.TemperatureF.Should().Be(72);
        second.HasMore.Should().BeFalse();
        (await queries.GetRawHistoryAsync("davis-test", start, start.AddMinutes(2))).Records.Should().HaveCount(2);
        var archiveFirst = await queries.GetArchiveHistoryAsync("davis-test", start, end, 2);
        archiveFirst.HasMore.Should().BeTrue();
        var archiveSecond = await queries.GetArchiveHistoryAsync("davis-test", start, end, 2, archiveFirst.NextAfterUtc);
        archiveSecond.Records.Should().ContainSingle().Which.RainfallInches.Should().Be(.1);
        archiveSecond.Records[0].RainRateInchesPerHour.Should().Be(.2);
        archiveSecond.Records[0].ArchiveIntervalMinutes.Should().Be(5);
        archiveSecond.Records[0].HighTemperatureF.Should().Be(75);
        archiveSecond.Records[0].ConsoleRecordedAtLocal.Kind.Should().Be(DateTimeKind.Unspecified);
        archiveSecond.HasMore.Should().BeFalse();
        scope.ServiceProvider.GetRequiredService<HvoV9DbContext>().ChangeTracker.Entries().Should().BeEmpty();
        clock.Now = new DateTimeOffset(At.AddMinutes(8));
        (await queries.GetCurrentAsync("davis-test")).Availability.Should().Be(V9WeatherAvailability.Stale);
    }

    private static DavisWeatherLivePayload Live(string station, DateTime at, double? temperature) => new()
    {
        StationId = station, RecordedAtUtc = at, TemperatureF = temperature,
        WindGust10MinMph = 12, WindSpeedMph = 8, BarometricPressureInHg = 29.92
    };

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class WeatherApplication(SqlServerDatabase database, TimeProvider clock) : WebApplicationFactory<Program>
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
                var seed = services.FirstOrDefault(descriptor => descriptor.ImplementationType == typeof(ApiKeySeedService));
                if (seed is not null) services.Remove(seed);
                services.RemoveAll<HvoV9DbContext>();
                services.AddScoped(_ => database.CreateContext());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            });
        }
    }
}

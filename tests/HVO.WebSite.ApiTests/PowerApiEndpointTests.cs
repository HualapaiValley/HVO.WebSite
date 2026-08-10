using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
    public async Task IngestReadings_AcceptsPinnedLegacyV1JsonShape()
    {
        var sourceId = $"legacy-power-api-test-{Guid.NewGuid():N}";
        var json = $$"""
            [{"sourceId":"{{sourceId}}","sourceSystem":"solarassistant","deviceId":"total","recordedAtUtc":"2026-05-23T08:05:00Z","pvPowerW":1200,"loadPowerW":900,"gridPowerW":-50,"batteryPowerW":-250,"systemPowerW":1000,"batteryStateOfChargePercent":82,"batteryVoltageV":53.2,"batteryCurrentA":-4.7,"batteryCapacityKwh":30.72,"gridVoltageV":240,"gridFrequencyHz":60,"outputVoltageV":120,"outputFrequencyHz":60,"loadPercentage":23,"inverterMode":"Battery","outputSourcePriority":"SBU","chargerSourcePriority":"Solar first"}]
            """;
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/power/readings", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        var row = db.PowerReadings.Single(reading => reading.SourceId == sourceId);
        row.BatteryPowerW.Should().Be(-250);
        row.BatteryCurrentA.Should().Be(-4.7);
        row.OutputSourcePriority.Should().Be("SBU");
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

    [TestMethod]
    public async Task InventoryAndConfigurationEndpoints_RequireScopesAndReturnLatestSnapshots()
    {
        var sourceId = $"solarassistant-{Guid.NewGuid():N}";
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var inventoryResponse = await _client.PostAsJsonAsync("/api/v1/power/device-inventory", new PowerDeviceInventoryPayload
        {
            SourceId = sourceId,
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            RestMetricCount = 124,
            MqttEntityCount = 48,
            MqttStateTopicCount = 42,
            Devices = [new PowerDeviceInventoryDevice { DeviceId = "eg4-6500ex", Name = "EG4 6500EX", Model = "6500EX" }],
        });
        var configurationResponse = await _client.PostAsJsonAsync("/api/v1/power/configuration", new PowerConfigurationPayload
        {
            SourceId = sourceId,
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            Settings = [new PowerConfigurationSetting { Key = "inverter_1.output_source_priority", Name = "Output source priority", Value = "Solar/Battery" }],
            CommandCapabilities = [new PowerCommandCapability { Key = "inverter_1.output_source_priority", Name = "Output source priority", CommandTopic = "solar_assistant/inverter_1/output_source_priority/set" }],
        });

        inventoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        configurationResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);
        var latestInventoryResponse = await _client.GetAsync($"/api/v1/power/device-inventory/latest?sourceId={sourceId}");
        var latestConfigurationResponse = await _client.GetAsync($"/api/v1/power/configuration/latest?sourceId={sourceId}");

        latestInventoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latestInventory = await latestInventoryResponse.Content.ReadFromJsonAsync<PowerDeviceInventorySnapshotResponse>();
        latestInventory!.Devices.Single().Model.Should().Be("6500EX");

        latestConfigurationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latestConfiguration = await latestConfigurationResponse.Content.ReadFromJsonAsync<PowerConfigurationSnapshotResponse>();
        latestConfiguration!.CommandCapabilities.Single().CommandTopic.Should().EndWith("/set");
    }

    [TestMethod]
    public async Task EnergyAndInverterDetailEndpoints_RequireScopesAndReturnLatestSnapshots()
    {
        var sourceId = $"solarassistant-{Guid.NewGuid():N}";
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var energyResponse = await _client.PostAsJsonAsync("/api/v1/power/energy", new PowerEnergyPayload
        {
            SourceId = sourceId,
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = DateTime.UtcNow,
            Counters = [new PowerEnergyCounter { Key = "pv_energy", Name = "PV energy", ValueKwh = 123.4 }],
        });
        var detailResponse = await _client.PostAsJsonAsync("/api/v1/power/inverter-detail", new PowerInverterDetailPayload
        {
            SourceId = sourceId,
            SourceSystem = "solarassistant",
            DeviceId = "inverter_1",
            RecordedAtUtc = DateTime.UtcNow,
            PvStrings = [new PowerPvStringDetail { StringId = "1", PowerW = 600 }],
        });

        energyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);
        var latestEnergyResponse = await _client.GetAsync($"/api/v1/power/energy/latest?sourceId={sourceId}");
        var latestDetailResponse = await _client.GetAsync($"/api/v1/power/inverter-detail/latest?sourceId={sourceId}");

        latestEnergyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latestEnergy = await latestEnergyResponse.Content.ReadFromJsonAsync<PowerEnergySnapshotResponse>();
        latestEnergy!.Counters.Single().ValueKwh.Should().Be(123.4);

        latestDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latestDetail = await latestDetailResponse.Content.ReadFromJsonAsync<PowerInverterDetailSnapshotResponse>();
        latestDetail!.PvStrings.Single().PowerW.Should().Be(600);
    }

    [TestMethod]
    public async Task GatewayStatusEndpoint_RequiresScopesAndReturnsLatestSnapshot()
    {
        var sourceId = $"solarassistant-{Guid.NewGuid():N}";
        var observedAt = DateTime.UtcNow;
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);

        var ingestResponse = await _client.PostAsJsonAsync("/api/v1/power/gateway-status", new GatewayStatusPayload
        {
            SourceId = sourceId,
            SourceSystem = "solarassistant",
            DeviceId = "total",
            RecordedAtUtc = observedAt,
            Identity = new GatewayIdentity("solarassistant", "SolarAssistant Gateway", GatewayDomain.Power, sourceId, "total"),
            Health = new GatewayHealthSnapshot(
                GatewayHealthState.Healthy,
                observedAt,
                [],
                GatewaySampleState.Live,
                OutboxState: "healthy",
                ApiSyncState: "healthy"),
            Rest = new GatewayRuntimeSignal(GatewaySampleState.Live, observedAt, Detail: "124 REST metric(s)"),
            Mqtt = new GatewayRuntimeSignal(GatewaySampleState.Live, observedAt, Detail: "48 entit(ies), 42 state topic(s)"),
            Outbox = new GatewayOutboxStatus(PendingCount: 0, FailedCount: 0, LastSentAtUtc: observedAt, LastBatchCount: 1),
            RestMetricCount = 124,
            MqttEntityCount = 48,
            MqttStateTopicCount = 42,
            MqttCommandTopicCount = 14,
        });

        ingestResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);
        var latestResponse = await _client.GetAsync($"/api/v1/power/gateway-status/latest?sourceId={sourceId}");

        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await latestResponse.Content.ReadFromJsonAsync<GatewayStatusSnapshotResponse>(JsonOptions);
        latest!.Health!.State.Should().Be(GatewayHealthState.Healthy);
        latest.Rest!.State.Should().Be(GatewaySampleState.Live);
        latest.MqttCommandTopicCount.Should().Be(14);

        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);
        var forbiddenRead = await _client.GetAsync($"/api/v1/power/gateway-status/latest?sourceId={sourceId}");
        forbiddenRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task MpptDetailEndpoints_RequireScopesAndReturnLatestAndRecentSnapshots()
    {
        var sourceId = $"mppt-{Guid.NewGuid():N}";
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);
        var ingestResponse = await _client.PostAsJsonAsync("/api/v1/power/mppt-detail", new PowerMpptDetailPayload
        {
            SourceId = sourceId,
            SourceSystem = "eg4-mppt100-48hv",
            DeviceId = "mppt-a",
            RecordedAtUtc = DateTime.UtcNow,
            Trackers = [new PowerMpptTrackerDetail { TrackerId = "pv-1", Name = "Array A", PowerW = 800, Provenance = PowerObservationProvenance.Direct }],
            BatteryOutput = new PowerMpptBatteryOutputDetail { CurrentA = 0, PowerW = 0, Provenance = PowerObservationProvenance.Direct },
        });
        ingestResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", ReadPlaintext);
        var latestResponse = await _client.GetAsync($"/api/v1/power/mppt-detail/latest?sourceId={sourceId}");
        var recentResponse = await _client.GetAsync($"/api/v1/power/mppt-detail/recent?sourceId={sourceId}&limit=1");
        var missingSourceResponse = await _client.GetAsync("/api/v1/power/mppt-detail/latest");
        var invalidLimitResponse = await _client.GetAsync("/api/v1/power/mppt-detail/recent?limit=5001");

        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = await latestResponse.Content.ReadFromJsonAsync<PowerMpptDetailSnapshotResponse>();
        latest!.Trackers.Single().PowerW.Should().Be(800);
        recentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recent = await recentResponse.Content.ReadFromJsonAsync<List<PowerMpptDetailSnapshotResponse>>();
        recent.Should().ContainSingle();
        missingSourceResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidLimitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", IngestPlaintext);
        var forbiddenRead = await _client.GetAsync($"/api/v1/power/mppt-detail/latest?sourceId={sourceId}");
        forbiddenRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static PowerReadingPayload ValidPayload(
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

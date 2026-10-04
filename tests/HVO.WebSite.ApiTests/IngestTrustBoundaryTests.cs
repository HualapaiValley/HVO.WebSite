using System.Net;
using System.Net.Http.Json;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Contracts.Weather;
using HVO.WebSite.v9.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.WebSite.ApiTests;

[TestClass]
public sealed class IngestTrustBoundaryTests
{
    internal static readonly string[] Routes = ["power/readings", "power/device-inventory", "power/configuration", "power/energy",
        "power/inverter-detail", "power/inverter-detail/batch", "power/mppt-detail", "power/mppt-detail/batch",
        "power/gateway-status", "power/smartshunt-observations/batch", "weather/raw", "weather/raw/batch", "weather/archive/batch"];
    private static readonly DateTime Recorded = new(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<object[]> Matrix() => Routes.SelectMany(route =>
        new[] { "missing", "invalid", "wrong-scope", "wrong-owner", "owner", "legacy", "owned-legacy", "case-alias", "legacy-case-alias", "homeassistant", "trimmed-owner", "mixed" }
        .Where(scenario => (scenario != "mixed" || IsBatch(route)) && (scenario != "homeassistant" || route != "weather/archive/batch") && (scenario != "trimmed-owner" || !route.Contains("smartshunt", StringComparison.Ordinal))).SelectMany(scenario => (route is "power/readings" or "weather/raw/batch" ? new[] { false, true } : new[] { false })
            .Select(cloudEvents => new object[] { route, scenario, cloudEvents })));

    [TestMethod]
    [DynamicData(nameof(Matrix))]
    public async Task EverySourceWriteEnforcesAuthorityBeforePersistence(string route, string scenario, bool cloudEvents)
    {
        using var factory = new IngestTrustTestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        await SeedAsync(db);
        var source = scenario switch { "legacy" => "legacy-unreserved", "owned-legacy" => "legacy-owned", "case-alias" => "GOVEE:matrix", "legacy-case-alias" => "LEGACY-OWNED", "homeassistant" => "ha-unclaimed", "trimmed-owner" => " govee:matrix ", _ => "govee:matrix" };
        var key = scenario switch { "missing" => null, "invalid" => "invalid-key", "wrong-scope" => "reader", "owner" or "trimmed-owner" or "mixed" => "owner", _ => "writer" };
        if (key is not null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        object body = Payload(route, source, scenario == "homeassistant" ? "homeassistant-generic" : "solarassistant");
        if (scenario == "mixed") body = new[] { SinglePayload(route, source), SinglePayload(route, "govee:other") };
        if (cloudEvents) body = ((object[])body).Select(data => new { specversion = "1.0", source = "urn:forged-envelope-owner", data }).ToArray();
        var response = await client.PostAsJsonAsync("/api/v1/" + route, body);
        var expected = scenario switch
        {
            "missing" or "invalid" => HttpStatusCode.Unauthorized,
            "owner" => HttpStatusCode.Created,
            "trimmed-owner" => HttpStatusCode.Created,
            "legacy" when !route.Contains("smartshunt", StringComparison.Ordinal) => HttpStatusCode.Created,
            _ => HttpStatusCode.Forbidden
        };
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        var writes = await CountWritesAsync(db);
        if (expected == HttpStatusCode.Created) Assert.IsTrue(writes > 0, "Authorized valid payload must persist telemetry.");
        else Assert.AreEqual(0, writes, "Denied request must leave every telemetry table empty.");
    }

    [TestMethod]
    public void MatrixCoversEveryDiscoveredSourceBearingPostAction()
    {
        using var factory = new IngestTrustTestFactory();
        var discovered = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(action =>
                action.AttributeRouteInfo?.Template?.StartsWith("api/v{version:apiVersion}/", StringComparison.Ordinal) == true
                && action.ControllerTypeInfo.AsType() != typeof(BmsController) // DeviceAddress contract, explicitly outside source IDs.
                && action.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                    .Any(constraint => constraint.HttpMethods.Contains("POST")) == true)
            .Select(action => action.AttributeRouteInfo!.Template!.Replace("api/v{version:apiVersion}/", "", StringComparison.Ordinal)).Order().ToArray();
        CollectionAssert.AreEquivalent(Routes, discovered);
    }

    [TestMethod]
    [DataRow(-1, true)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    public async Task ActiveSourceReservationUsesInjectedInclusiveUtcExpiry(int expiryTicks, bool allowed)
    {
        using var factory = new IngestTrustTestFactory { Clock = new FixedClock(new DateTimeOffset(Recorded)) };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
        await SeedAsync(db);
        var owner = await db.ApiKeys.SingleAsync(key => key.Name == "other-owner");
        owner.ExpiresAt = DateTime.SpecifyKind(Recorded.AddTicks(expiryTicks), DateTimeKind.Unspecified);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add("X-Api-Key", "writer");
        var response = await client.PostAsJsonAsync("/api/v1/weather/raw", Payload("weather/raw", "legacy-owned", "davis"));
        Assert.AreEqual(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(allowed ? 1 : 0, await CountWritesAsync(db));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal static async Task SeedAsync(HvoV9DbContext db)
    {
        foreach (var name in new[] { "writer", "reader", "owner", "other-owner" })
        {
            var key = new ApiKey { Id = Guid.NewGuid(), Name = name, KeyHash = HVO.WebSite.v9.Middleware.ApiKeyAuthMiddleware.HashKey(name), IsActive = true };
            key.Claims.Add(new ApiKeyClaim { ClaimType = "scope", ClaimValue = name == "reader" ? "read:power" : "ingest:power", ApiKeyId = key.Id });
            if (name != "reader") key.Claims.Add(new ApiKeyClaim { ClaimType = "scope", ClaimValue = "ingest:weather", ApiKeyId = key.Id });
            if (name == "owner") key.Claims.Add(new ApiKeyClaim { ClaimType = "source", ClaimValue = "govee:matrix", ApiKeyId = key.Id });
            if (name == "other-owner") foreach (var source in new[] { "govee:other", "legacy-owned" })
                key.Claims.Add(new ApiKeyClaim { ClaimType = "source", ClaimValue = source, ApiKeyId = key.Id });
            db.ApiKeys.Add(key);
        }
        await db.SaveChangesAsync();
    }

    internal static bool IsBatch(string route) => route.EndsWith("batch", StringComparison.Ordinal) || route == "power/readings";
    internal static object Payload(string route, string source, string system) => IsBatch(route) ? new[] { SinglePayload(route, source, system) } : SinglePayload(route, source, system);
    internal static object SinglePayload(string route, string source, string system = "solarassistant")
    {
        var common = new Dictionary<string, object?> { ["sourceId"] = source, ["sourceSystem"] = system, ["deviceId"] = "total", ["recordedAtUtc"] = Recorded };
        switch (route)
        {
            case "weather/raw": case "weather/raw/batch": return new { stationId = source, sourceSystem = system, recordedAt = Recorded, temperatureF = 70 };
            case "weather/archive/batch": return new DavisWeatherArchivePayload { StationId = source, RecordedAtUtc = Recorded, ConsoleRecordedAtLocal = DateTime.SpecifyKind(Recorded, DateTimeKind.Unspecified), ArchiveIntervalMinutes = 5 };
            case "power/smartshunt-observations/batch": return new SmartShuntObservationPayload(
                new PowerReadingPayload { SourceId = source, SourceSystem = "victron-smartshunt", DeviceId = "battery", RecordedAtUtc = Recorded, BatteryVoltageV = 52, BatteryCurrentA = -5, BatteryPowerW = -260, BatteryStateOfChargePercent = 80 },
                new SmartShuntDetailPayload { SourceId = source, SourceSystem = "victron-smartshunt", DeviceId = "battery", RecordedAtUtc = Recorded, ConsumedAh = -20 });
            case "power/gateway-status": common["identity"] = new GatewayIdentity("gateway", "Gateway", GatewayDomain.Power, source, "total"); common["health"] = new GatewayHealthSnapshot(GatewayHealthState.Healthy, Recorded, [], GatewaySampleState.Live); break;
            case "power/mppt-detail": case "power/mppt-detail/batch": common["trackers"] = new[] { new PowerMpptTrackerDetail { TrackerId = "pv-1", Name = "Array", Provenance = PowerObservationProvenance.Direct } }; break;
            case "power/readings": common["batteryVoltageV"] = 52; break;
        }
        return common;
    }

    internal static async Task<int> CountWritesAsync(HvoV9DbContext db) =>
        await db.PowerReadings.CountAsync() + await db.PowerDeviceInventorySnapshots.CountAsync()
        + await db.PowerConfigurationSnapshots.CountAsync() + await db.PowerEnergySnapshots.CountAsync()
        + await db.PowerInverterDetailSnapshots.CountAsync() + await db.PowerMpptDetailSnapshots.CountAsync()
        + await db.GatewayStatusSnapshots.CountAsync() + await db.SmartShuntDetailSnapshots.CountAsync()
        + await db.WeatherRaw.CountAsync() + await db.WeatherArchive.CountAsync();
}

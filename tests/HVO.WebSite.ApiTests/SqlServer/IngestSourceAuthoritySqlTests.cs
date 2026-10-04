using System.Net;
using System.Net.Http.Json;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using static HVO.WebSite.ApiTests.IngestTrustBoundaryTests;

namespace HVO.WebSite.ApiTests.SqlServer;

[TestClass]
[TestCategory("Integration")]
[TestCategory("SqlServerIntegration")]
[DoNotParallelize]
public sealed class IngestSourceAuthoritySqlTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> RouteMatrix() => Routes.Select(route => new object[] { route });

    [TestMethod]
    [DynamicData(nameof(RouteMatrix))]
    public async Task EverySourceRoute_DeniesWrongOwnersBeforeSqlWritesAndPersistsAuthorizedRequests(string route)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        using var factory = Application(database);
        using var client = Client(factory);
        var exactOnly = route.Contains("smartshunt", StringComparison.Ordinal);

        foreach (var (key, source) in new[]
        {
            ("writer", "govee:matrix"), ("owner", "GOVEE:matrix"),
            ("writer", "LEGACY-OWNED"), ("writer", "  legacy-owned  ")
        })
        {
            // SmartShunt rejects untrimmed payload identities during validation,
            // before source authority; this matrix supplies valid write shapes.
            if (exactOnly && source != source.Trim()) continue;
            await PostAsync(client, route, key, Payload(route, source, "solarassistant"), HttpStatusCode.Forbidden);
            Assert.AreEqual(0, await CountWritesAsync(db), $"{route}: {key}/{source} must not persist telemetry.");
        }

        if (IsBatch(route))
        {
            object[] mixed = [SinglePayload(route, "govee:matrix"), SinglePayload(route, "govee:other")];
            await PostAsync(client, route, "owner", mixed, HttpStatusCode.Forbidden);
            Assert.AreEqual(0, await CountWritesAsync(db), "Whole mixed batch must be denied before its authorized item is saved.");
            if (route is "power/readings" or "weather/raw/batch")
            {
                var envelopes = mixed.Select(data => new { specversion = "1.0", source = "urn:forged-owner", data }).ToArray();
                await PostAsync(client, route, "owner", envelopes, HttpStatusCode.Forbidden);
                Assert.AreEqual(0, await CountWritesAsync(db));
            }
        }

        await PostAsync(client, route, "writer", Payload(route, "legacy-unreserved", "solarassistant"),
            exactOnly ? HttpStatusCode.Forbidden : HttpStatusCode.Created);
        var legacyWrites = await CountWritesAsync(db);
        if (exactOnly) Assert.AreEqual(0, legacyWrites);
        else Assert.IsTrue(legacyWrites > 0, "The authorized legacy request must really persist.");

        await PostAsync(client, route, "owner", Payload(route, "govee:matrix", "solarassistant"), HttpStatusCode.Created);
        Assert.IsTrue(await CountWritesAsync(db) > legacyWrites, "The exact owner's request must really persist.");
    }

    [TestMethod]
    [DataRow("Latin1_General_100_CI_AS", "  LEGACY-OWNED  ", "legacy-owned")]
    [DataRow("Latin1_General_100_CS_AS", "LEGACY-OWNED", "legacy-owned")]
    [DataRow("Latin1_General_100_CI_AI", "café-owned", "cafe-owned")]
    public async Task SqlClaimCollationAndTrimAliases_CannotBypassAnotherActiveOwner(string collation, string claim, string requested)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        // Only fixed fixture collations are admitted; this never alters application databases.
        var alter = collation switch
        {
            "Latin1_General_100_CI_AS" => "ALTER TABLE v9.ApiKeyClaim ALTER COLUMN ClaimValue nvarchar(256) COLLATE Latin1_General_100_CI_AS NOT NULL",
            "Latin1_General_100_CS_AS" => "ALTER TABLE v9.ApiKeyClaim ALTER COLUMN ClaimValue nvarchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL",
            "Latin1_General_100_CI_AI" => "ALTER TABLE v9.ApiKeyClaim ALTER COLUMN ClaimValue nvarchar(256) COLLATE Latin1_General_100_CI_AI NOT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(collation))
        };
        await db.Database.ExecuteSqlRawAsync(alter);
        var actual = await db.Database.SqlQueryRaw<string>(
            "SELECT collation_name AS Value FROM sys.columns WHERE object_id=OBJECT_ID(N'v9.ApiKeyClaim') AND name=N'ClaimValue'").SingleAsync();
        Assert.AreEqual(collation, actual);
        await SeedAsync(db);
        var ownerClaim = await db.ApiKeyClaims.SingleAsync(row => row.ClaimType == "source" && row.ClaimValue == "legacy-owned");
        ownerClaim.ClaimValue = claim;
        await db.SaveChangesAsync();

        using var factory = Application(database);
        using var client = Client(factory);
        await PostAsync(client, "weather/raw", "writer", Payload("weather/raw", requested, "davis"), HttpStatusCode.Forbidden);
        Assert.AreEqual(0, await CountWritesAsync(db));
    }

    [TestMethod]
    public async Task CollationEquivalentReservationOnAnotherKey_DeniesEvenAnExactClaimant()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        var other = await db.ApiKeys.SingleAsync(key => key.Name == "other-owner");
        db.ApiKeyClaims.Add(new ApiKeyClaim { ApiKeyId = other.Id, ClaimType = "source", ClaimValue = " GOVEE:MATRIX " });
        await db.SaveChangesAsync();
        using var factory = Application(database);
        using var client = Client(factory);
        await PostAsync(client, "weather/raw", "owner", Payload("weather/raw", "govee:matrix", "davis"), HttpStatusCode.Forbidden);
        Assert.AreEqual(0, await CountWritesAsync(db));
    }

    [TestMethod]
    [DataRow(-1, true)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    public async Task SqlMaterializedReservation_UsesInclusiveUtcExpiry(int expiryTicks, bool allowed)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        var owner = await db.ApiKeys.SingleAsync(key => key.Name == "other-owner");
        owner.ExpiresAt = At.UtcDateTime.AddTicks(expiryTicks);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.AreEqual(DateTimeKind.Unspecified, (await db.ApiKeys.SingleAsync(key => key.Name == "other-owner")).ExpiresAt!.Value.Kind);
        using var factory = Application(database);
        using var client = Client(factory);
        await PostAsync(client, "weather/raw", "writer", Payload("weather/raw", "legacy-owned", "davis"),
            allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
        Assert.AreEqual(allowed ? 1 : 0, await CountWritesAsync(db));
    }

    [TestMethod]
    public async Task SqlCredentialExpiry_IsIdenticalForPrimedAndEmptyAuthenticationCaches()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        var writer = await db.ApiKeys.SingleAsync(key => key.Name == "writer");
        writer.ExpiresAt = At.UtcDateTime.AddSeconds(1);
        await db.SaveChangesAsync();
        var clock = new TestClock(At);
        using var cachedApplication = Application(database, clock);
        using var cached = Client(cachedApplication);
        await PostAsync(cached, "weather/raw", "writer", Payload("weather/raw", "before-expiry", "davis"), HttpStatusCode.Created);

        clock.Now = At.AddSeconds(1);
        using var freshApplication = Application(database, clock);
        using var fresh = Client(freshApplication);
        foreach (var client in new[] { cached, fresh })
            await PostAsync(client, "weather/raw", "writer", Payload("weather/raw", "at-expiry", "davis"), HttpStatusCode.Unauthorized);
        Assert.AreEqual(1, await CountWritesAsync(db), "Expired requests must not add telemetry.");
    }

    private static IngestTrustTestFactory Application(SqlServerDatabase database, TimeProvider? clock = null) => new()
    {
        ContextFactory = () => database.CreateContext(),
        Clock = clock ?? new TestClock(At)
    };

    private static HttpClient Client(IngestTrustTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task PostAsync(HttpClient client, string route, string key, object payload, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + route) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Api-Key", key);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(expected, response.StatusCode, $"{route}: {await response.Content.ReadAsStringAsync()}");
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

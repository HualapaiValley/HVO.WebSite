using FluentAssertions;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class ApiKeyMiddlewareTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HvoV9DbContext CreateDbContext(string name) =>
        new(new DbContextOptionsBuilder<HvoV9DbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static IMemoryCache CreateCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private static (ApiKeyAuthMiddleware middleware, WasCalledTracker tracker) BuildMiddleware(
        IMemoryCache? cache = null)
    {
        var tracker = new WasCalledTracker();
        var next = new RequestDelegate(ctx =>
        {
            tracker.Called = true;
            return Task.CompletedTask;
        });
        var middleware = new ApiKeyAuthMiddleware(
            next,
            cache ?? CreateCache(),
            NullLogger<ApiKeyAuthMiddleware>.Instance);
        return (middleware, tracker);
    }

    private static async Task<ApiKey> SeedKeyAsync(
        HvoV9DbContext db,
        string plaintext,
        bool isActive = true,
        DateTime? expiresAt = null,
        ApiKeyType type = ApiKeyType.System,
        string[]? scopes = null,
        ApiKeyOwner? owner = null)
    {
        if (owner is not null)
            db.ApiKeyOwners.Add(owner);

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "test-key",
            KeyHash = ApiKeyAuthMiddleware.HashKey(plaintext),
            Type = type,
            IsActive = isActive,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            OwnerId = owner?.Id
        };

        if (scopes is not null)
        {
            foreach (var scope in scopes)
                key.Claims.Add(new ApiKeyClaim { ClaimType = "scope", ClaimValue = scope });
        }

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();
        return key;
    }

    private static DefaultHttpContext BuildContext(string? apiKeyHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream(); // writable body for response writes
        if (apiKeyHeader is not null)
            ctx.Request.Headers["X-Api-Key"] = apiKeyHeader;
        return ctx;
    }

    private sealed class WasCalledTracker
    {
        public bool Called { get; set; }
    }

    // -------------------------------------------------------------------------
    // HashKey
    // -------------------------------------------------------------------------

    [TestMethod]
    public void HashKey_ReturnsDeterministicLowercaseHex()
    {
        var hash1 = ApiKeyAuthMiddleware.HashKey("my-secret-key");
        var hash2 = ApiKeyAuthMiddleware.HashKey("my-secret-key");

        hash1.Should().Be(hash2);
        hash1.Should().MatchRegex("^[0-9a-f]{64}$", "SHA-256 hex is 64 lowercase hex chars");
    }

    [TestMethod]
    public void HashKey_ProducesDifferentHashesForDifferentInputs()
    {
        var hash1 = ApiKeyAuthMiddleware.HashKey("key-alpha");
        var hash2 = ApiKeyAuthMiddleware.HashKey("key-beta");

        hash1.Should().NotBe(hash2);
    }

    [TestMethod]
    public void HashKey_IsCaseSensitive()
    {
        var lower = ApiKeyAuthMiddleware.HashKey("mykey");
        var upper = ApiKeyAuthMiddleware.HashKey("MYKEY");

        lower.Should().NotBe(upper);
    }

    // -------------------------------------------------------------------------
    // No header — pass through
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task InvokeAsync_CallsNext_WhenNoApiKeyHeader()
    {
        var (middleware, tracker) = BuildMiddleware();
        await using var db = CreateDbContext(nameof(InvokeAsync_CallsNext_WhenNoApiKeyHeader));
        var ctx = BuildContext(apiKeyHeader: null);

        await middleware.InvokeAsync(ctx, db);

        tracker.Called.Should().BeTrue();
        ctx.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_CallsNext_WhenApiKeyHeaderIsWhitespace()
    {
        var (middleware, tracker) = BuildMiddleware();
        await using var db = CreateDbContext(nameof(InvokeAsync_CallsNext_WhenApiKeyHeaderIsWhitespace));
        var ctx = BuildContext(apiKeyHeader: "   ");

        await middleware.InvokeAsync(ctx, db);

        tracker.Called.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Invalid key → 401 short-circuit
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task InvokeAsync_Returns401_WhenKeyNotFoundInDatabase()
    {
        var (middleware, tracker) = BuildMiddleware();
        await using var db = CreateDbContext(nameof(InvokeAsync_Returns401_WhenKeyNotFoundInDatabase));
        var ctx = BuildContext("not-a-real-key");

        await middleware.InvokeAsync(ctx, db);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        tracker.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_Returns401_WhenKeyIsInactive()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_Returns401_WhenKeyIsInactive));
        await SeedKeyAsync(db, "inactive-key", isActive: false);

        var (middleware, tracker) = BuildMiddleware();
        var ctx = BuildContext("inactive-key");

        await middleware.InvokeAsync(ctx, db);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        tracker.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task InvokeAsync_Returns401_WhenKeyIsExpired()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_Returns401_WhenKeyIsExpired));
        await SeedKeyAsync(db, "expired-key", expiresAt: DateTime.UtcNow.AddDays(-1));

        var (middleware, tracker) = BuildMiddleware();
        var ctx = BuildContext("expired-key");

        await middleware.InvokeAsync(ctx, db);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        tracker.Called.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Valid key → user set correctly
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task InvokeAsync_SetsAuthenticatedUser_WhenKeyIsValid()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_SetsAuthenticatedUser_WhenKeyIsValid));
        await SeedKeyAsync(db, "valid-key");

        var (middleware, tracker) = BuildMiddleware();
        var ctx = BuildContext("valid-key");

        await middleware.InvokeAsync(ctx, db);

        tracker.Called.Should().BeTrue();
        ctx.User.Identity!.IsAuthenticated.Should().BeTrue();
        ctx.User.Identity.AuthenticationType.Should().Be("ApiKey");
    }

    [TestMethod]
    public async Task InvokeAsync_IncludesScopeClaims_WhenKeyHasScopes()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_IncludesScopeClaims_WhenKeyHasScopes));
        await SeedKeyAsync(db, "scoped-key", scopes: ["ingest:weather", "read:weather"]);

        var (middleware, _) = BuildMiddleware();
        var ctx = BuildContext("scoped-key");

        await middleware.InvokeAsync(ctx, db);

        var scopes = ctx.User.FindAll("scope").Select(c => c.Value).ToList();
        scopes.Should().Contain("ingest:weather");
        scopes.Should().Contain("read:weather");
    }

    [TestMethod]
    public async Task InvokeAsync_SetsApiKeyTypeClaim_ForSystemKey()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_SetsApiKeyTypeClaim_ForSystemKey));
        await SeedKeyAsync(db, "system-key", type: ApiKeyType.System);

        var (middleware, _) = BuildMiddleware();
        var ctx = BuildContext("system-key");

        await middleware.InvokeAsync(ctx, db);

        ctx.User.FindFirst("api_key_type")!.Value.Should().Be("System");
    }

    [TestMethod]
    public async Task InvokeAsync_IncludesOwnerClaims_ForUserTypeKey()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_IncludesOwnerClaims_ForUserTypeKey));

        var owner = new ApiKeyOwner
        {
            Id = Guid.NewGuid(),
            EntraObjectId = "entra-oid-abc123",
            DisplayName = "Jane Dev",
            Email = "jane@example.com",
            CreatedAt = DateTime.UtcNow
        };

        await SeedKeyAsync(db, "user-key", type: ApiKeyType.User, owner: owner);

        var (middleware, _) = BuildMiddleware();
        var ctx = BuildContext("user-key");

        await middleware.InvokeAsync(ctx, db);

        ctx.User.FindFirst("entra_object_id")!.Value.Should().Be("entra-oid-abc123");
        ctx.User.FindFirst("display_name")!.Value.Should().Be("Jane Dev");
        ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value.Should().Be("jane@example.com");
    }

    [TestMethod]
    public async Task InvokeAsync_StillAcceptsKey_WhenExpiresAtIsInFuture()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_StillAcceptsKey_WhenExpiresAtIsInFuture));
        await SeedKeyAsync(db, "future-expiry-key", expiresAt: DateTime.UtcNow.AddDays(30));

        var (middleware, tracker) = BuildMiddleware();
        var ctx = BuildContext("future-expiry-key");

        await middleware.InvokeAsync(ctx, db);

        tracker.Called.Should().BeTrue();
        ctx.User.Identity!.IsAuthenticated.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Cache
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task InvokeAsync_ServesFromCache_OnSecondRequest()
    {
        await using var db = CreateDbContext(nameof(InvokeAsync_ServesFromCache_OnSecondRequest));
        var key = await SeedKeyAsync(db, "cached-key", scopes: ["read:weather"]);

        var cache = CreateCache();
        var (middleware, _) = BuildMiddleware(cache);

        // First request — populates cache
        var ctx1 = BuildContext("cached-key");
        await middleware.InvokeAsync(ctx1, db);
        ctx1.User.Identity!.IsAuthenticated.Should().BeTrue();

        // Remove the key from DB — second request must still succeed via cache
        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync();

        var ctx2 = BuildContext("cached-key");
        await middleware.InvokeAsync(ctx2, db);

        ctx2.User.Identity!.IsAuthenticated.Should().BeTrue("cache should serve the principal");
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HVO.WebSite.v9.Middleware;

/// <summary>
/// Authenticates requests that present an X-Api-Key header.
/// The key is SHA-256 hashed and looked up in the v9.ApiKey table.
/// A short-lived memory cache avoids a DB hit on every request.
/// </summary>
public class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string AuthScheme = "ApiKey";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private readonly TimeProvider _timeProvider;

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<ApiKeyAuthMiddleware> logger,
        TimeProvider timeProvider)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task InvokeAsync(HttpContext context, HvoV9DbContext db)
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
        {
            if (RequiresApiKeyForProtectedApiEndpoint(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Missing API key.");
                return;
            }

            await _next(context);
            return;
        }

        var keyHash = HashKey(rawKey.ToString().Trim());
        var cacheKey = $"apikey:{keyHash}";

        if (!_cache.TryGetValue(cacheKey, out CachedAuthentication? authentication)
            || authentication is null
            || authentication.ValidUntil <= _timeProvider.GetUtcNow())
        {
            authentication = await BuildAuthenticationAsync(db, keyHash, context.RequestAborted);

            // MemoryCache may use a different clock. The explicit deadline above is
            // authoritative, including in tests that advance only the injected clock.
            var remaining = authentication.ValidUntil - _timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                _cache.Set(cacheKey, authentication, remaining);
            }
            else
            {
                _cache.Remove(cacheKey);
            }
        }

        // Recheck after the database work too: a credential may expire while its
        // principal/LastUsedAt is being loaded, and equality means expired.
        if (authentication.Principal is null
            || authentication.ExpiresAt is { } expiry && expiry <= _timeProvider.GetUtcNow())
        {
            _logger.LogWarning("Invalid or inactive API key presented from {RemoteIp}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or inactive API key.");
            return;
        }

        context.User = authentication.Principal;
        await _next(context);
    }

    private async Task<CachedAuthentication> BuildAuthenticationAsync(HvoV9DbContext db, string keyHash, CancellationToken ct)
    {
        var apiKey = await db.ApiKeys
            .Include(k => k.Claims)
            .Include(k => k.Owner)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive, ct);

        var now = _timeProvider.GetUtcNow();
        // ExpiresAt is stored UTC; SQL datetime2 materializes it as Unspecified.
        DateTimeOffset? expiresAt = apiKey?.ExpiresAt is { } value
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : null;
        if (apiKey is null || expiresAt is { } expiry && expiry <= now)
            return new(null, expiresAt, now.AddSeconds(30));

        await UpdateLastUsedAsync(db, apiKey.Id, ct);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, apiKey.Name),
            new("api_key_id", apiKey.Id.ToString()),
            new("api_key_type", apiKey.Type.ToString())
        };

        foreach (var claim in apiKey.Claims)
        {
            claims.Add(new Claim(claim.ClaimType, claim.ClaimValue));
        }

        // For User-type keys, attach Entra owner metadata
        if (apiKey.Type == ApiKeyType.User && apiKey.Owner is not null)
        {
            claims.Add(new Claim("entra_object_id", apiKey.Owner.EntraObjectId));
            if (apiKey.Owner.Email is not null)
                claims.Add(new Claim(ClaimTypes.Email, apiKey.Owner.Email));
            if (apiKey.Owner.DisplayName is not null)
                claims.Add(new Claim("display_name", apiKey.Owner.DisplayName));
        }

        var identity = new ClaimsIdentity(claims, AuthScheme);
        var validUntil = now.Add(CacheTtl);
        if (expiresAt is { } deadline && deadline < validUntil)
            validUntil = deadline;
        return new(new ClaimsPrincipal(identity), expiresAt, validUntil);
    }

    private async Task UpdateLastUsedAsync(HvoV9DbContext db, Guid keyId, CancellationToken ct)
    {
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await db.ApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
        }
        catch (Exception ex)
        {
            // Non-critical — don't fail the request
            _logger.LogDebug(ex, "Failed to update LastUsedAt for API key {ApiKeyId}", keyId);
        }
    }

    public static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexStringLower(bytes);
    }

    private sealed record CachedAuthentication(
        ClaimsPrincipal? Principal,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset ValidUntil);

    private static bool RequiresApiKeyForProtectedApiEndpoint(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return false;

        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
    }
}

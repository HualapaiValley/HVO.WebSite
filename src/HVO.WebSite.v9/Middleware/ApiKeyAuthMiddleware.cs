using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
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

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, HvoV9DbContext db)
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
        {
            await _next(context);
            return;
        }

        var keyHash = HashKey(rawKey.ToString().Trim());
        var cacheKey = $"apikey:{keyHash}";

        if (!_cache.TryGetValue(cacheKey, out ClaimsPrincipal? principal))
        {
            principal = await BuildPrincipalAsync(db, keyHash, context.RequestAborted);

            if (principal is not null)
            {
                _cache.Set(cacheKey, principal, CacheTtl);
            }
            else
            {
                // Cache negative result briefly to reduce DB load from invalid keys
                _cache.Set(cacheKey, (ClaimsPrincipal?)null, TimeSpan.FromSeconds(30));
            }
        }

        if (principal is null)
        {
            _logger.LogWarning("Invalid or inactive API key presented from {RemoteIp}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or inactive API key.");
            return;
        }

        context.User = principal;
        await _next(context);
    }

    private async Task<ClaimsPrincipal?> BuildPrincipalAsync(HvoV9DbContext db, string keyHash, CancellationToken ct)
    {
        var apiKey = await db.ApiKeys
            .Include(k => k.Claims)
            .Include(k => k.Owner)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive, ct);

        if (apiKey is null)
            return null;

        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < DateTime.UtcNow)
            return null;

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
        return new ClaimsPrincipal(identity);
    }

    private async Task UpdateLastUsedAsync(HvoV9DbContext db, Guid keyId, CancellationToken ct)
    {
        try
        {
            await db.ApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow), ct);
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
}

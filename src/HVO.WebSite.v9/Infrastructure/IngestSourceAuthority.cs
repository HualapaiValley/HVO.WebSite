using System.Security.Claims;
using HVO.DataModels.Data;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Infrastructure;

internal static class IngestSourceAuthority
{
    public const string SourceClaimType = "source";

    public static async Task<bool> CanWriteAllAsync(
        HvoV9DbContext db,
        ClaimsPrincipal principal,
        IEnumerable<(string? SourceId, string? SourceSystem)> sources,
        TimeProvider clock,
        CancellationToken cancellationToken,
        bool requireExactOwner = false)
    {
        var claims = principal.FindAll(SourceClaimType).Select(static claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        Guid.TryParse(principal.FindFirst("api_key_id")?.Value, out var keyId);
        var now = clock.GetUtcNow().UtcDateTime;
        var requested = sources.Where(static source => !string.IsNullOrWhiteSpace(source.SourceId))
            .Select(static source => (SourceId: source.SourceId!.Trim(), SourceSystem: source.SourceSystem?.Trim()))
            .Distinct().ToArray();
        foreach (var source in requested)
        {
            var reserved = source.SourceId.StartsWith("kasa:", StringComparison.OrdinalIgnoreCase)
                || source.SourceId.StartsWith("govee:", StringComparison.OrdinalIgnoreCase)
                || source.SourceSystem?.StartsWith("homeassistant-", StringComparison.OrdinalIgnoreCase) == true;
            if ((requireExactOwner || reserved) && !claims.Contains(source.SourceId)) return false;
        }

        // Database-side equality retains its actual collation (including accent
        // equivalences). Case folding additionally protects provider-independent
        // and legacy-case aliases. Check the whole batch before any persistence.
        var sourceIds = requested.Select(static source => source.SourceId).Distinct().ToArray();
        var normalizedIds = sourceIds.Select(static source => source.ToUpperInvariant()).ToArray();
        return !await db.ApiKeyClaims.AsNoTracking().AnyAsync(claim =>
            claim.ClaimType == SourceClaimType
            && (sourceIds.Contains(claim.ClaimValue.Trim()) || normalizedIds.Contains(claim.ClaimValue.Trim().ToUpper()))
            && claim.ApiKeyId != keyId && claim.ApiKey.IsActive
            && (!claim.ApiKey.ExpiresAt.HasValue || claim.ApiKey.ExpiresAt > now), cancellationToken);
    }
}

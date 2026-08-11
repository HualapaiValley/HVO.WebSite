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
        CancellationToken cancellationToken)
    {
        var sourceClaims = principal.FindAll(SourceClaimType).Select(static claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        var requested = sources.Where(static source => !string.IsNullOrWhiteSpace(source.SourceId)).Distinct().ToArray();
        foreach (var source in requested)
        {
            var reservedSource = source.SourceId!.StartsWith("kasa:", StringComparison.Ordinal)
                || source.SourceId.StartsWith("govee:", StringComparison.Ordinal);
            var homeAssistantSource = source.SourceSystem?.StartsWith("homeassistant-", StringComparison.OrdinalIgnoreCase) == true;
            if ((reservedSource || homeAssistantSource) && !sourceClaims.Contains(source.SourceId))
                return false;
        }

        var unownedSourceIds = requested.Select(static source => source.SourceId!)
            .Where(sourceId => !sourceClaims.Contains(sourceId)).Distinct().ToArray();
        return !await db.ApiKeyClaims.AsNoTracking().AnyAsync(
            claim => claim.ClaimType == SourceClaimType
                && unownedSourceIds.Contains(claim.ClaimValue)
                && claim.ApiKey.IsActive
                && (!claim.ApiKey.ExpiresAt.HasValue || claim.ApiKey.ExpiresAt > DateTime.UtcNow),
            cancellationToken);
    }
}

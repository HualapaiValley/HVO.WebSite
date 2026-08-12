using System.Security.Cryptography;
using System.Text;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.WebSite.v9.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

/// <summary>
/// Runs at startup to ensure system API keys defined in configuration exist in the database.
/// Safe to run multiple times — existing keys are never duplicated or modified.
/// </summary>
public sealed class ApiKeySeedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeySeedService> _logger;

    public ApiKeySeedService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<ApiKeySeedService> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();

        // Apply any pending v9 migrations before seeding.
        // Guard is required so integration tests using InMemory providers don't throw;
        // InMemory databases don't need migrations — the test factory calls EnsureCreated instead.
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync(cancellationToken);

        await SeedSystemKeyAsync(
            db,
            configurationKeyName: "Seeding:DavisApiKey",
            rawKey: _configuration["Seeding:DavisApiKey"],
            name: "Davis Vantage Pro 2 — ingest",
            scopes: [ApiScopes.WeatherIngest],
            cancellationToken);

        await SeedSmartShuntKeyAsync(db, cancellationToken);

        await SeedSystemKeyAsync(
            db,
            configurationKeyName: "Seeding:BmsApiKey",
            rawKey: _configuration["Seeding:BmsApiKey"],
            name: "JK BMS — ingest",
            scopes: [ApiScopes.BmsIngest],
            cancellationToken);

        await SeedSystemKeyAsync(
            db,
            configurationKeyName: "Seeding:PowerApiKey",
            rawKey: _configuration["Seeding:PowerApiKey"],
            name: "Power gateway — ingest",
            scopes: [ApiScopes.PowerIngest],
            cancellationToken);

        await SeedSystemKeyAsync(
            db,
            configurationKeyName: "Seeding:WeatherReadApiKey",
            rawKey: _configuration["Seeding:WeatherReadApiKey"],
            name: "Weather API — read",
            scopes: [ApiScopes.WeatherRead],
            cancellationToken);

        await SeedSystemKeyAsync(
            db,
            configurationKeyName: "Seeding:PowerReadApiKey",
            rawKey: _configuration["Seeding:PowerReadApiKey"],
            name: "Power API — read",
            scopes: [ApiScopes.PowerRead],
            cancellationToken);

        await SeedHomeAssistantExporterKeyAsync(db, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedSystemKeyAsync(
        HvoV9DbContext db,
        string configurationKeyName,
        string? rawKey,
        string name,
        string[] scopes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            _logger.LogDebug("{ConfigurationKeyName} is not configured — skipping API key seed for '{Name}'", configurationKeyName, name);
            return;
        }

        var keyHash = ComputeSha256Hex(rawKey);

        var exists = await db.ApiKeys.AnyAsync(k => k.KeyHash == keyHash, ct);
        if (exists)
        {
            _logger.LogDebug("API key '{Name}' already exists — skipping seed", name);
            return;
        }

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            KeyHash = keyHash,
            Name = name,
            Type = ApiKeyType.System,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        apiKey.Claims = scopes.Select(s => new ApiKeyClaim
        {
            ApiKeyId = apiKey.Id,
            ClaimType = "scope",
            ClaimValue = s,
        }).ToList();

        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Seeded API key '{Name}' (id={Id}) with scopes: {Scopes}",
            name, apiKey.Id, string.Join(", ", scopes));
    }

    private async Task SeedHomeAssistantExporterKeyAsync(HvoV9DbContext db, CancellationToken cancellationToken)
    {
        const string configurationKeyName = "Seeding:HomeAssistantExporterApiKey";
        var rawKey = _configuration[configurationKeyName];
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            _logger.LogDebug("{ConfigurationKeyName} is not configured — skipping Home Assistant exporter API key seed", configurationKeyName);
            return;
        }

        var sources = _configuration.GetSection("Seeding:HomeAssistantExporterSources").Get<string[]>() ?? [];
        sources = sources.Select(static source => source.Trim()).Where(static source => source.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (sources.Length == 0 || sources.Any(static source => source.Length > 64
            || !(source.StartsWith("kasa:", StringComparison.Ordinal) || source.StartsWith("govee:", StringComparison.Ordinal))))
            throw new InvalidOperationException("Seeding:HomeAssistantExporterSources must contain reserved kasa:/govee: source IDs no longer than 64 characters.");

        var keyHash = ComputeSha256Hex(rawKey);
        var apiKey = await db.ApiKeys.Include(static key => key.Claims).SingleOrDefaultAsync(key => key.KeyHash == keyHash, cancellationToken);
        if (apiKey is null)
        {
            apiKey = new ApiKey
            {
                Id = Guid.NewGuid(),
                KeyHash = keyHash,
                Name = "Home Assistant exporter — ingest",
                Type = ApiKeyType.System,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.ApiKeys.Add(apiKey);
        }

        var requiredClaims = new[]
        {
            (Type: "scope", Value: ApiScopes.PowerIngest),
            (Type: "scope", Value: ApiScopes.WeatherIngest)
        }.Concat(sources.Select(static source => (Type: IngestSourceAuthority.SourceClaimType, Value: source)));
        var configuredSources = sources.ToHashSet(StringComparer.Ordinal);
        var staleSourceClaims = apiKey.Claims
            .Where(claim => claim.ClaimType == IngestSourceAuthority.SourceClaimType
                && !configuredSources.Contains(claim.ClaimValue))
            .ToArray();
        if (staleSourceClaims.Length > 0)
        {
            db.ApiKeyClaims.RemoveRange(staleSourceClaims);
            foreach (var claim in staleSourceClaims)
                apiKey.Claims.Remove(claim);
        }
        var conflictingSource = await db.ApiKeyClaims.AsNoTracking().FirstOrDefaultAsync(
            claim => claim.ClaimType == IngestSourceAuthority.SourceClaimType
                && sources.Contains(claim.ClaimValue)
                && claim.ApiKeyId != apiKey.Id
                && claim.ApiKey.IsActive
                && (!claim.ApiKey.ExpiresAt.HasValue || claim.ApiKey.ExpiresAt > DateTime.UtcNow),
            cancellationToken);
        if (conflictingSource is not null)
            throw new InvalidOperationException("A configured Home Assistant exporter source is already reserved by another active API key.");
        foreach (var claim in requiredClaims.Where(required => !apiKey.Claims.Any(
            existing => existing.ClaimType == required.Type && existing.ClaimValue == required.Value)))
        {
            apiKey.Claims.Add(new ApiKeyClaim
            {
                ApiKeyId = apiKey.Id,
                ClaimType = claim.Type,
                ClaimValue = claim.Value
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Ensured Home Assistant exporter API key with {SourceCount} source claim(s)", sources.Length);
    }

    private async Task SeedSmartShuntKeyAsync(HvoV9DbContext db, CancellationToken cancellationToken)
    {
        const string keyName = "Seeding:SmartShuntApiKey";
        const string sourceName = "Seeding:SmartShuntSourceId";
        var rawKey = _configuration[keyName];
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            _logger.LogDebug("{ConfigurationKeyName} is not configured - skipping SmartShunt API key seed", keyName);
            return;
        }
        var sourceId = _configuration[sourceName]?.Trim();
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 64)
            throw new InvalidOperationException($"{sourceName} must be a source ID of 1-64 characters when {keyName} is configured.");

        var keyHash = ComputeSha256Hex(rawKey);
        var apiKey = await db.ApiKeys.Include(static key => key.Claims).SingleOrDefaultAsync(key => key.KeyHash == keyHash, cancellationToken);
        if (apiKey is null)
        {
            apiKey = new ApiKey { Id = Guid.NewGuid(), KeyHash = keyHash, Name = "Victron SmartShunt - ingest", Type = ApiKeyType.System, IsActive = true, CreatedAt = DateTime.UtcNow };
            db.ApiKeys.Add(apiKey);
        }
        var conflict = await db.ApiKeyClaims.AsNoTracking().AnyAsync(claim => claim.ClaimType == IngestSourceAuthority.SourceClaimType
            && claim.ClaimValue == sourceId && claim.ApiKeyId != apiKey.Id && claim.ApiKey.IsActive
            && (!claim.ApiKey.ExpiresAt.HasValue || claim.ApiKey.ExpiresAt > DateTime.UtcNow), cancellationToken);
        if (conflict) throw new InvalidOperationException("The configured SmartShunt source is already reserved by another active API key.");
        var staleSourceClaims = apiKey.Claims
            .Where(claim => claim.ClaimType == IngestSourceAuthority.SourceClaimType && claim.ClaimValue != sourceId)
            .ToArray();
        if (staleSourceClaims.Length > 0)
        {
            db.ApiKeyClaims.RemoveRange(staleSourceClaims);
            foreach (var claim in staleSourceClaims)
                apiKey.Claims.Remove(claim);
        }
        var required = new[] { (Type: "scope", Value: ApiScopes.PowerIngest), (Type: IngestSourceAuthority.SourceClaimType, Value: sourceId) };
        foreach (var claim in required.Where(requiredClaim => !apiKey.Claims.Any(claim => claim.ClaimType == requiredClaim.Type && claim.ClaimValue == requiredClaim.Value)))
            apiKey.Claims.Add(new ApiKeyClaim { ApiKeyId = apiKey.Id, ClaimType = claim.Type, ClaimValue = claim.Value });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}

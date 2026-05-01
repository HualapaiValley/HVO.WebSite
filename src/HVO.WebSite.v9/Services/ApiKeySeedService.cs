using System.Security.Cryptography;
using System.Text;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
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
            rawKey: _configuration["Seeding:DavisApiKey"],
            name: "Davis Vantage Pro 2 — ingest",
            scopes: ["ingest:weather"],
            cancellationToken);

        await SeedSystemKeyAsync(
            db,
            rawKey: _configuration["Seeding:BmsApiKey"],
            name: "JK BMS — ingest",
            scopes: ["ingest:bms"],
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedSystemKeyAsync(
        HvoV9DbContext db,
        string? rawKey,
        string name,
        string[] scopes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            _logger.LogDebug("Seeding:DavisApiKey is not configured — skipping API key seed for '{Name}'", name);
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

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}

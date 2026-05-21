using System.ComponentModel;
using System.Text.Json;
using HVO.DataModels.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HVO.WebSite.v9.Services;

public sealed class SiteConfigurationService : ISiteConfigurationService
{
    private const string CacheKey = "site-configuration:snapshot";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private readonly HvoV9DbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SiteConfigurationService> _logger;

    public SiteConfigurationService(
        HvoV9DbContext dbContext,
        IMemoryCache cache,
        ILogger<SiteConfigurationService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.TryGetValue(key, out var value) ? value : null;
    }

    public async Task<T?> GetValueAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, cancellationToken);
        if (value is null)
            return default;

        return ConvertValue<T>(key, value);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var values = await _dbContext.SiteConfiguration
                .AsNoTracking()
                .OrderBy(x => x.Key)
                .ToDictionaryAsync(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            _logger.LogDebug("Loaded {Count} site configuration values from the database.", values.Count);

            return (IReadOnlyDictionary<string, string>)values;
        });

        return snapshot ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Invalidate(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _logger.LogDebug("Invalidating cached site configuration snapshot after change to key {Key}.", key);
        _cache.Remove(CacheKey);
    }

    public void InvalidateAll()
    {
        _logger.LogDebug("Invalidating cached site configuration snapshot.");
        _cache.Remove(CacheKey);
    }

    private T ConvertValue<T>(string key, string rawValue)
    {
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        try
        {
            if (targetType == typeof(string))
                return (T)(object)rawValue;

            if (targetType.IsEnum)
                return (T)Enum.Parse(targetType, rawValue, ignoreCase: true);

            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter.CanConvertFrom(typeof(string)))
            {
                var converted = converter.ConvertFromInvariantString(rawValue);
                if (converted is not null)
                    return (T)converted;
            }

            var deserialized = JsonSerializer.Deserialize(rawValue, targetType);
            if (deserialized is not null)
                return (T)deserialized;
        }
        catch (Exception ex) when (ex is FormatException or InvalidEnumArgumentException or JsonException or NotSupportedException)
        {
            _logger.LogError(ex, "Failed to convert site configuration key {Key} value '{Value}' to type {Type}.", key, rawValue, targetType.Name);
            throw new InvalidOperationException(
                $"Site configuration key '{key}' could not be converted to {targetType.Name}.",
                ex);
        }

        throw new InvalidOperationException(
            $"Site configuration key '{key}' could not be converted to {targetType.Name}.");
    }
}
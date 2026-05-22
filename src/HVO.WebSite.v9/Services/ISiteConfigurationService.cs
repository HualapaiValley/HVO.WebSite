namespace HVO.WebSite.v9.Services;

public interface ISiteConfigurationService
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    Task<T?> GetValueAsync<T>(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetSnapshotAsync(CancellationToken cancellationToken = default);

    void Invalidate(string key);

    void InvalidateAll();
}
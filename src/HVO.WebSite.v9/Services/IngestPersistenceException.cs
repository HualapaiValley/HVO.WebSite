namespace HVO.WebSite.v9.Services;

// Persistence failures are retryable transport failures, never permanent per-record validation failures.
internal sealed class IngestPersistenceException(string message, Exception? inner = null) : Exception(message, inner);

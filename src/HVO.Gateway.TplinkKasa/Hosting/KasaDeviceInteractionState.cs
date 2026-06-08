using System.Collections.Concurrent;

namespace HVO.Gateway.TplinkKasa.Hosting;

public sealed class KasaDeviceInteractionState
{
    private readonly ConcurrentDictionary<string, int> _holds = new(StringComparer.OrdinalIgnoreCase);

    public IDisposable? TryBegin(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        var normalizedSourceId = sourceId.Trim();
        _holds.AddOrUpdate(normalizedSourceId, 1, (_, count) => count + 1);
        return new InteractionHold(this, normalizedSourceId);
    }

    public bool IsSuspended(string? sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId) && _holds.ContainsKey(sourceId);

    private void End(string sourceId)
    {
        _holds.AddOrUpdate(sourceId, 0, (_, count) => Math.Max(0, count - 1));
        if (_holds.TryGetValue(sourceId, out var count) && count <= 0)
        {
            _holds.TryRemove(sourceId, out _);
        }
    }

    private sealed class InteractionHold(KasaDeviceInteractionState owner, string sourceId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.End(sourceId);
            }
        }
    }
}
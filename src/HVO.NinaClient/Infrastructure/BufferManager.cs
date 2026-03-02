using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HVO.NinaClient.Infrastructure;

/// <summary>
/// Memory-efficient buffer manager using ArrayPool for WebSocket operations
/// </summary>
public sealed class BufferManager : IDisposable
{
    private readonly ArrayPool<byte> _arrayPool;
    private readonly ConcurrentBag<RentedBuffer> _rentedBuffers = new();
    private readonly ILogger<BufferManager>? _logger;
    private bool _disposed;

    public BufferManager(ILogger<BufferManager>? logger = null)
    {
        _arrayPool = ArrayPool<byte>.Shared;
        _logger = logger;
        
        _logger?.LogTrace("BufferManager initialized with shared ArrayPool");
    }

    /// <summary>
    /// Rent a buffer from the pool
    /// </summary>
    /// <param name="minimumSize">Minimum required buffer size</param>
    /// <returns>Rented buffer wrapper</returns>
    public RentedBuffer RentBuffer(int minimumSize)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BufferManager));

        var buffer = _arrayPool.Rent(minimumSize);
        var rentedBuffer = new RentedBuffer(buffer, minimumSize, this);
        
        _rentedBuffers.Add(rentedBuffer);
        
        _logger?.LogTrace("Rented buffer of size {ActualSize} (requested: {RequestedSize})", 
            buffer.Length, minimumSize);

        return rentedBuffer;
    }

    /// <summary>
    /// Return a buffer to the pool
    /// </summary>
    /// <param name="buffer">Buffer to return</param>
    /// <param name="clearArray">Whether to clear the array</param>
    internal void ReturnBuffer(byte[] buffer, bool clearArray = true)
    {
        if (_disposed)
        {
            _logger?.LogTrace("Buffer manager disposed, cannot return buffer");
            return;
        }

        try
        {
            _arrayPool.Return(buffer, clearArray);
            _logger?.LogTrace("Returned buffer of size {Size} to pool", buffer.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to return buffer to pool");
        }
    }

    /// <summary>
    /// Gets statistics about buffer usage
    /// </summary>
    /// <returns>Buffer usage statistics</returns>
    public BufferStatistics GetStatistics()
    {
        var activeBuffers = _rentedBuffers.Count(b => !b.IsDisposed);
        var totalRented = _rentedBuffers.Count;

        return new BufferStatistics
        {
            ActiveBuffers = activeBuffers,
            TotalRentedBuffers = totalRented,
            DisposedBuffers = totalRented - activeBuffers
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        var stats = GetStatistics();
        if (stats.ActiveBuffers > 0)
        {
            _logger?.LogWarning("Disposing BufferManager with {ActiveBuffers} active buffers", stats.ActiveBuffers);
        }

        _logger?.LogDebug("BufferManager disposed - Total rented: {Total}, Active: {Active}", 
            stats.TotalRentedBuffers, stats.ActiveBuffers);
    }
}

/// <summary>
/// Wrapper for a rented buffer that automatically returns it when disposed
/// </summary>
public sealed class RentedBuffer : IDisposable
{
    private readonly BufferManager _manager;
    private readonly bool _clearOnReturn;
    private bool _disposed;

    internal RentedBuffer(byte[] buffer, int usableLength, BufferManager manager, bool clearOnReturn = true)
    {
        Buffer = buffer;
        UsableLength = usableLength;
        _manager = manager;
        _clearOnReturn = clearOnReturn;
    }

    /// <summary>
    /// The actual buffer array
    /// </summary>
    public byte[] Buffer { get; }

    /// <summary>
    /// The usable length of the buffer (may be less than Buffer.Length)
    /// </summary>
    public int UsableLength { get; }

    /// <summary>
    /// Gets whether this buffer has been disposed
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets a memory view of the usable portion of the buffer
    /// </summary>
    public Memory<byte> Memory => Buffer.AsMemory(0, UsableLength);

    /// <summary>
    /// Gets a span view of the usable portion of the buffer
    /// </summary>
    public Span<byte> Span => Buffer.AsSpan(0, UsableLength);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _manager.ReturnBuffer(Buffer, _clearOnReturn);
    }
}

/// <summary>
/// Statistics about buffer usage
/// </summary>
public record BufferStatistics
{
    /// <summary>
    /// Number of currently active (not disposed) buffers
    /// </summary>
    public int ActiveBuffers { get; init; }

    /// <summary>
    /// Total number of buffers rented since creation
    /// </summary>
    public int TotalRentedBuffers { get; init; }

    /// <summary>
    /// Number of buffers that have been disposed
    /// </summary>
    public int DisposedBuffers { get; init; }
}

/// <summary>
/// Extension methods for memory-efficient operations
/// </summary>
public static class BufferExtensions
{
    /// <summary>
    /// Efficiently copy data to a rented buffer
    /// </summary>
    /// <param name="buffer">Target buffer</param>
    /// <param name="source">Source data</param>
    /// <param name="offset">Offset in the buffer</param>
    /// <returns>Number of bytes copied</returns>
    public static int CopyFrom(this RentedBuffer buffer, ReadOnlySpan<byte> source, int offset = 0)
    {
        var availableSpace = Math.Min(source.Length, buffer.UsableLength - offset);
        source[..availableSpace].CopyTo(buffer.Span[offset..]);
        return availableSpace;
    }

    /// <summary>
    /// Get a slice of the buffer as ReadOnlyMemory
    /// </summary>
    /// <param name="buffer">Source buffer</param>
    /// <param name="start">Start index</param>
    /// <param name="length">Length of slice</param>
    /// <returns>ReadOnlyMemory slice</returns>
    public static ReadOnlyMemory<byte> Slice(this RentedBuffer buffer, int start, int length)
    {
        var actualLength = Math.Min(length, buffer.UsableLength - start);
        return buffer.Memory.Slice(start, actualLength);
    }
}
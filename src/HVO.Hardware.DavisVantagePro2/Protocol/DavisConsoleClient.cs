using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.DavisVantagePro2.Protocol;

/// <summary>
/// Low-level TCP wrapper for the Davis WeatherLink IP adapter.
/// Implements the same primitives as the WeeWX EthernetWrapper class.
/// Thread safety: serialize all calls with an external SemaphoreSlim.
/// </summary>
public sealed class DavisConsoleClient : IDisposable
{
    private readonly ILogger<DavisConsoleClient> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _socketTimeout;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private bool _disposed;

    public bool IsConnected => _tcp?.Connected == true && _stream != null;

    public DavisConsoleClient(string host, int port, TimeSpan socketTimeout, ILogger<DavisConsoleClient> logger)
    {
        _host = host;
        _port = port;
        _socketTimeout = socketTimeout;
        _logger = logger;
    }

    // ── Connection ───────────────────────────────────────────────────────────

    public async Task OpenAsync(CancellationToken ct)
    {
        CloseInternal();
        _tcp = new TcpClient();
        _tcp.ReceiveTimeout = (int)_socketTimeout.TotalMilliseconds;
        _tcp.SendTimeout    = (int)_socketTimeout.TotalMilliseconds;
        await _tcp.ConnectAsync(_host, _port, ct);
        _stream = _tcp.GetStream();
        _logger.LogInformation("Connected to Davis console at {Host}:{Port}", _host, _port);
    }

    public void Close()
    {
        // Cancel any pending LOOP before closing
        try { WriteRaw([DavisProtocol.Lf]); } catch { /* best-effort */ }
        CloseInternal();
    }

    private void CloseInternal()
    {
        _stream?.Close();
        _tcp?.Close();
        _stream = null;
        _tcp    = null;
    }

    // ── Wake sequence ────────────────────────────────────────────────────────

    /// <summary>
    /// Wake the Davis console using the three-step wake sequence.
    /// Also cancels any pending LOOP command in progress.
    /// </summary>
    public async Task WakeAsync(int maxTries = 3, CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= maxTries; attempt++)
        {
            try
            {
                FlushOutput();
                await FlushInputAsync(ct);

                await WriteAsync(DavisProtocol.WakeBytes, ct);
                await Task.Delay(500, ct);
                await FlushInputAsync(ct);

                await WriteAsync(DavisProtocol.WakeNl, ct);
                byte[] response = await ReadExactAsync(2, ct);

                if (response[0] == DavisProtocol.Lf && response[1] == DavisProtocol.Cr)
                {
                    _logger.LogDebug("Console woke up on attempt {Attempt}", attempt);
                    return;
                }

                _logger.LogDebug("Wake attempt {Attempt}: unexpected response {R0:X2} {R1:X2}", attempt, response[0], response[1]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("Wake attempt {Attempt} failed: {Ex}", attempt, ex.Message);
            }

            if (attempt < maxTries)
                await Task.Delay(1200, ct);
        }

        throw new DavisWakeupException($"Unable to wake Davis console after {maxTries} attempts");
    }

    // ── Primitive I/O ─────────────────────────────────────────────────────────

    /// <summary>Read exactly <paramref name="count"/> bytes.</summary>
    public async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        EnsureConnected();
        var buffer = new byte[count];
        int received = 0;
        while (received < count)
        {
            int n = await _stream!.ReadAsync(buffer.AsMemory(received, count - received), ct);
            if (n == 0)
                throw new DavisException("Connection closed by console");
            received += n;
        }
        return buffer;
    }

    /// <summary>Write bytes to the console, with the required TCP send delay.</summary>
    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        EnsureConnected();
        await _stream!.WriteAsync(data, ct);
        await Task.Delay(TimeSpan.FromSeconds(DavisProtocol.TcpSendDelaySeconds), ct);
    }

    // ── Console-level commands ───────────────────────────────────────────────

    /// <summary>
    /// Send data and wait for ACK. Throws <see cref="DavisProtocolException"/> if no ACK received.
    /// </summary>
    public async Task SendDataAsync(byte[] data, CancellationToken ct)
    {
        await WriteAsync(data, ct);
        byte[] ack = await ReadExactAsync(1, ct);
        if (ack[0] != DavisProtocol.Ack)
            throw new DavisProtocolException($"Expected ACK (0x06), got 0x{ack[0]:X2}");
    }

    /// <summary>
    /// Send data with appended CRC and wait for ACK, retrying up to <paramref name="maxTries"/> times.
    /// </summary>
    public async Task SendDataWithCrc16Async(byte[] data, CancellationToken ct, int maxTries = 3)
    {
        byte[] withCrc = CrcCalculator.AppendCrc(data);

        for (int attempt = 1; attempt <= maxTries; attempt++)
        {
            try
            {
                await WriteAsync(withCrc, ct);
                byte[] ack = await ReadExactAsync(1, ct);
                if (ack[0] == DavisProtocol.Ack) return;
                _logger.LogDebug("SendDataWithCrc16 attempt {A}: bad ACK 0x{B:X2}", attempt, ack[0]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("SendDataWithCrc16 attempt {A}: {Ex}", attempt, ex.Message);
            }
        }

        throw new DavisCrcException($"Unable to send data with CRC after {maxTries} tries");
    }

    /// <summary>
    /// Send a command string (e.g. "GETTIME\n"), wake the console first.
    /// Returns the response lines after the "OK" prefix.
    /// </summary>
    public async Task<string[]> SendCommandAsync(string command, CancellationToken ct, int maxTries = 3)
    {
        byte[] cmdBytes = Encoding.ASCII.GetBytes(command);

        for (int attempt = 1; attempt <= maxTries; attempt++)
        {
            try
            {
                await WakeAsync(maxTries, ct);
                await WriteAsync(cmdBytes, ct);
                await Task.Delay(500, ct); // console reaction time

                // Read all queued bytes
                byte[] raw = await ReadQueuedAsync(ct);
                string response = Encoding.ASCII.GetString(raw).Trim();
                string[] lines = response.Split(["\n\r", "\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length > 0 && lines[0] == "OK")
                    return lines[1..];

                _logger.LogDebug("SendCommand attempt {A}: bad response '{R}'", attempt, lines.FirstOrDefault());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("SendCommand attempt {A}: {Ex}", attempt, ex.Message);
            }
        }

        throw new DavisRetriesExceededException($"Max retries exceeded for command '{command.TrimEnd()}'");
    }

    /// <summary>
    /// Read a validated CRC packet of <paramref name="totalBytes"/> (includes 2 CRC bytes).
    /// Optionally sends a prompt byte (e.g. ACK) before reading.
    /// </summary>
    public async Task<byte[]> GetDataWithCrc16Async(int totalBytes, CancellationToken ct, byte[]? prompt = null, int maxTries = 3)
    {
        if (prompt is not null)
            await WriteAsync(prompt, ct);

        bool first = true;
        byte[]? buffer = null;

        for (int attempt = 1; attempt <= maxTries; attempt++)
        {
            try
            {
                if (!first)
                    await WriteAsync([DavisProtocol.Nak], ct);

                buffer = await ReadExactAsync(totalBytes, ct);
                if (CrcCalculator.IsValid(buffer)) return buffer;

                _logger.LogDebug("GetDataWithCrc16 attempt {A}: CRC error", attempt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("GetDataWithCrc16 attempt {A}: {Ex}", attempt, ex.Message);
            }

            first = false;
        }

        if (buffer is not null)
            throw new DavisCrcException("CRC validation failed after max retries");

        throw new DavisException("No data received from console");
    }

    // ── Flush helpers ────────────────────────────────────────────────────────

    private void FlushOutput() { /* no-op for TCP — sendall never has leftover bytes */ }

    private async Task FlushInputAsync(CancellationToken ct)
    {
        try
        {
            // Read with zero-wait to drain any buffered input
            _tcp!.ReceiveTimeout = 1;
            var buf = new byte[4096];
            while (_stream!.DataAvailable)
                _ = await _stream.ReadAsync(buf, ct);
        }
        catch { /* ignore timeout */ }
        finally
        {
            if (_tcp is not null)
                _tcp.ReceiveTimeout = (int)_socketTimeout.TotalMilliseconds;
        }
    }

    private async Task<byte[]> ReadQueuedAsync(CancellationToken ct)
    {
        var buffer = new List<byte>(256);
        var tmp = new byte[256];
        // Read in a short loop while data is available
        while (_stream!.DataAvailable || buffer.Count == 0)
        {
            int n = await _stream.ReadAsync(tmp.AsMemory(0, 256), ct);
            if (n == 0) break;
            buffer.AddRange(tmp[..n]);
            if (!_stream.DataAvailable) break;
        }
        return [.. buffer];
    }

    // ── Low-level write (sync, for close path) ───────────────────────────────

    private void WriteRaw(byte[] data)
    {
        _stream?.Write(data, 0, data.Length);
        Thread.Sleep(50);
    }

    private void EnsureConnected()
    {
        if (_stream is null || _tcp?.Connected != true)
            throw new DavisException("Not connected to Davis console");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseInternal();
    }
}

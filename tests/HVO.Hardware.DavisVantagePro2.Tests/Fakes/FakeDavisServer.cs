using System.Net;
using System.Net.Sockets;

namespace HVO.Hardware.DavisVantagePro2.Tests.Fakes;

/// <summary>
/// In-process TCP server that simulates a Davis WeatherLink IP adapter.
///
/// Script a sequence of interactions with <see cref="Step"/> — each step reads
/// a fixed number of bytes from the client then sends a pre-configured response.
/// The server processes all steps in order for the first accepted connection,
/// then holds the connection open until disposed.
///
/// Usage:
/// <code>
///   await using var server = new FakeDavisServer();
///   server.WakeStep()
///         .Step(8, PacketBuilder.AckThenData(loop2Packet))
///         .Start();
///   // ... connect client to server.Port and run test ...
/// </code>
/// </summary>
public sealed class FakeDavisServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly List<(int ReceiveCount, byte[] Response)> _steps = [];
    private Task? _serverTask;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Local TCP port the server is listening on.</summary>
    public int Port { get; }

    public FakeDavisServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Append a script step: read <paramref name="receiveCount"/> bytes from the client,
    /// then write <paramref name="response"/> to the client.
    /// Use <paramref name="receiveCount"/> = 0 to send the response immediately
    /// without reading any bytes first.
    /// Returns <c>this</c> for fluent chaining.
    /// </summary>
    public FakeDavisServer Step(int receiveCount, byte[] response)
    {
        _steps.Add((receiveCount, response));
        return this;
    }

    /// <summary>
    /// Append the standard Davis wake-response step:
    /// reads 4 newline bytes (\n\n\n + \n) from the client then sends \n\r.
    /// </summary>
    public FakeDavisServer WakeStep() => Step(4, [0x0A, 0x0D]);

    /// <summary>Begin accepting a single client connection and processing the script.</summary>
    public FakeDavisServer Start()
    {
        _serverTask = RunAsync(_cts.Token);
        return this;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync(ct);
            using NetworkStream stream = client.GetStream();

            var discard = new byte[4096];

            foreach (var (receiveCount, response) in _steps)
            {
                // Drain the expected bytes from the client (content not inspected)
                int received = 0;
                while (received < receiveCount)
                {
                    int n = await stream.ReadAsync(
                        discard.AsMemory(0, Math.Min(discard.Length, receiveCount - received)), ct);
                    if (n == 0) return; // client disconnected prematurely
                    received += n;
                }

                if (response.Length > 0)
                    await stream.WriteAsync(response, ct);
            }

            // Keep the socket alive until the test disposes the server so that
            // the client does not receive a spurious RST when it flushes on close.
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* swallow — the test will surface timeout / assertion failures */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { }
        }
        _listener.Stop();
        _cts.Dispose();
    }
}

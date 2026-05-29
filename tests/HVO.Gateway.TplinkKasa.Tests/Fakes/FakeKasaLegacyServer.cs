using HVO.Gateway.TplinkKasa.Protocol;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace HVO.Gateway.TplinkKasa.Tests.Fakes;

internal sealed class FakeKasaLegacyServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly ConcurrentDictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

    public FakeKasaLegacyServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public void RespondTo(string module, string command, string responseJson) =>
        _responses[$"{module}.{command}"] = responseJson;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient)
    {
        using (tcpClient)
        await using (var stream = tcpClient.GetStream())
        {
            var lengthBuffer = new byte[4];
            await ReadExactlyAsync(stream, lengthBuffer, _cts.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
            var payload = new byte[length];
            await ReadExactlyAsync(stream, payload, _cts.Token).ConfigureAwait(false);
            var requestJson = KasaFrameCodec.DecodeTcpPayload(payload);
            var response = ResolveResponse(requestJson);
            var frame = KasaFrameCodec.EncodeTcpFrame(response);
            await stream.WriteAsync(frame, _cts.Token).ConfigureAwait(false);
        }
    }

    private string ResolveResponse(string requestJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(requestJson);
        foreach (var module in document.RootElement.EnumerateObject())
        {
            foreach (var command in module.Value.EnumerateObject())
            {
                if (_responses.TryGetValue($"{module.Name}.{command.Name}", out var response))
                {
                    return response;
                }
            }
        }

        return "{\"system\":{\"get_sysinfo\":{\"err_code\":-1}}}";
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}

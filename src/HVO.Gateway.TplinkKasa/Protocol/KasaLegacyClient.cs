using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Protocol;

public sealed class KasaLegacyClient(TimeSpan timeout) : IKasaLegacyClient
{
    private const int MaxPayloadBytes = 1024 * 1024;

    public async Task<JsonDocument> SendReadOnlyAsync(string host, int port, string commandJson, CancellationToken cancellationToken)
    {
        using var command = JsonDocument.Parse(commandJson);
        if (!KasaCommands.IsKnownReadOnly(command))
        {
            throw new InvalidOperationException("Only allowlisted read-only Kasa commands are supported by this prototype client.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);

        await using var stream = tcpClient.GetStream();
        var frame = KasaFrameCodec.EncodeTcpFrame(commandJson);
        await stream.WriteAsync(frame, timeoutCts.Token).ConfigureAwait(false);

        var lengthBuffer = new byte[4];
        await ReadExactlyAsync(stream, lengthBuffer, timeoutCts.Token).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (payloadLength <= 0 || payloadLength > MaxPayloadBytes)
        {
            throw new InvalidDataException($"Invalid Kasa TCP payload length: {payloadLength}.");
        }

        var payload = new byte[payloadLength];
        await ReadExactlyAsync(stream, payload, timeoutCts.Token).ConfigureAwait(false);
        var json = KasaFrameCodec.DecodeTcpPayload(payload);
        return JsonDocument.Parse(json);
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Kasa TCP stream ended before the complete frame was read.");
            }

            offset += read;
        }
    }
}

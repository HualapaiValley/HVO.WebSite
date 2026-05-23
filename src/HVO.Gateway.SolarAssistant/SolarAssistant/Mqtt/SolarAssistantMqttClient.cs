using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using HVO.Gateway.SolarAssistant.Configuration;

namespace HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

/// <summary>Minimal MQTT 3.1.1 read-only client for SolarAssistant discovery/state subscriptions.</summary>
public sealed class SolarAssistantMqttClient
{
    private const int KeepAliveSeconds = 30;
    private const int OperationTimeoutSeconds = 30;
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(KeepAliveSeconds / 2);
    private static readonly TimeSpan PingResponseTimeout = TimeSpan.FromSeconds(KeepAliveSeconds + 5);

    private readonly SolarAssistantOptions _options;

    public SolarAssistantMqttClient(SolarAssistantOptions options)
    {
        _options = options;
    }

    public async Task RunAsync(
        IReadOnlyList<string> subscriptions,
        Func<SolarAssistantMqttMessage, CancellationToken, Task> onMessage,
        Action onSubscribed,
        CancellationToken ct)
    {
        using var tcp = new TcpClient();

        try
        {
            await WithTimeoutAsync(
                tcp.ConnectAsync(_options.Host, _options.MqttPort, ct).AsTask(),
                TimeSpan.FromSeconds(OperationTimeoutSeconds),
                tcp.Dispose,
                "MQTT TCP connect timed out.",
                ct);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        await using var stream = tcp.GetStream();

        await ConnectAsync(stream, tcp.Dispose, ct);
        await SubscribeAsync(stream, subscriptions, tcp.Dispose, ct);
        onSubscribed();

        var lastPacketAt = DateTime.UtcNow;
        var awaitingPingResponse = false;
        var pingSentAt = DateTime.MinValue;
        var readTask = ReadPacketAsync(stream, ct);
        while (!ct.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(readTask, Task.Delay(PingInterval, ct));
            if (completed != readTask)
            {
                ct.ThrowIfCancellationRequested();
                if (awaitingPingResponse && DateTime.UtcNow - pingSentAt > PingResponseTimeout)
                {
                    tcp.Dispose();
                    throw new TimeoutException("MQTT broker did not respond to keepalive ping.");
                }

                await WritePacketAsync(stream, packetType: 12, flags: 0, [], [], ct);
                awaitingPingResponse = true;
                pingSentAt = DateTime.UtcNow;
                continue;
            }

            var packet = await readTask;
            if (packet is null)
                return;

            lastPacketAt = DateTime.UtcNow;
            readTask = ReadPacketAsync(stream, ct);

            var (packetType, flags, payload) = packet.Value;
            if (packetType == 13)
            {
                awaitingPingResponse = false;
                continue;
            }

            if (awaitingPingResponse && DateTime.UtcNow - lastPacketAt <= PingResponseTimeout)
                awaitingPingResponse = false;
            if (packetType != 3)
                continue;

            var message = ReadPublish(flags, payload);
            if (message is not null)
                await onMessage(message, ct);
        }
    }

    private async Task ConnectAsync(NetworkStream stream, Action onTimeout, CancellationToken ct)
    {
        var clientId = $"{_options.MqttClientId}-{Environment.MachineName}-{Environment.ProcessId}";
        var variableHeader = new List<byte>();
        WriteString(variableHeader, "MQTT");
        variableHeader.Add(4);

        byte flags = 0x02;
        if (!string.IsNullOrWhiteSpace(_options.MqttUsername))
        {
            flags |= 0x80;
            if (!string.IsNullOrEmpty(_options.MqttPassword))
                flags |= 0x40;
        }

        variableHeader.Add(flags);
        WriteUInt16(variableHeader, KeepAliveSeconds);

        var payload = new List<byte>();
        WriteString(payload, clientId);
        if (!string.IsNullOrWhiteSpace(_options.MqttUsername))
        {
            WriteString(payload, _options.MqttUsername);
            if (!string.IsNullOrEmpty(_options.MqttPassword))
                WriteString(payload, _options.MqttPassword);
        }

        await WritePacketAsync(stream, packetType: 1, flags: 0, variableHeader, payload, ct);

        var response = await ReadPacketWithTimeoutAsync(stream, onTimeout, "MQTT broker did not send CONNACK in time.", ct)
            ?? throw new InvalidOperationException("MQTT broker closed before CONNACK.");
        if (response.PacketType != 2 || response.Payload.Length < 2)
            throw new InvalidOperationException("MQTT broker returned an invalid CONNACK.");

        var returnCode = response.Payload[1];
        if (returnCode != 0)
            throw new InvalidOperationException($"MQTT CONNACK rejected connection with return code {returnCode}.");
    }

    private static async Task SubscribeAsync(NetworkStream stream, IReadOnlyList<string> subscriptions, Action onTimeout, CancellationToken ct)
    {
        var variableHeader = new List<byte>();
        WriteUInt16(variableHeader, 1);

        var payload = new List<byte>();
        foreach (var subscription in subscriptions)
        {
            WriteString(payload, subscription);
            payload.Add(0);
        }

        await WritePacketAsync(stream, packetType: 8, flags: 2, variableHeader, payload, ct);

        var response = await ReadPacketWithTimeoutAsync(stream, onTimeout, "MQTT broker did not send SUBACK in time.", ct)
            ?? throw new InvalidOperationException("MQTT broker closed before SUBACK.");
        if (response.PacketType != 9 || response.Payload.Length < 2)
            throw new InvalidOperationException("MQTT broker returned an invalid SUBACK.");

        var packetId = BinaryPrimitives.ReadUInt16BigEndian(response.Payload.AsSpan(0, 2));
        if (packetId != 1)
            throw new InvalidOperationException($"MQTT broker returned SUBACK for packet id {packetId}.");

        var returnCodes = response.Payload.AsSpan(2);
        if (returnCodes.Length != subscriptions.Count)
            throw new InvalidOperationException("MQTT broker returned an invalid SUBACK return-code count.");

        for (var i = 0; i < returnCodes.Length; i++)
        {
            var code = returnCodes[i];
            if (code == 0x80)
                throw new InvalidOperationException($"MQTT broker rejected subscription '{subscriptions[i]}'.");
            if (code > 2)
                throw new InvalidOperationException($"MQTT broker returned unsupported SUBACK code {code} for '{subscriptions[i]}'.");
        }
    }

    private static SolarAssistantMqttMessage? ReadPublish(byte flags, byte[] payload)
    {
        if (payload.Length < 2)
            return null;

        var topicLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(0, 2));
        if (payload.Length < 2 + topicLength)
            return null;

        var offset = 2 + topicLength;
        var qos = (flags & 0x06) >> 1;
        if (qos > 0)
        {
            if (payload.Length < offset + 2)
                return null;
            offset += 2;
        }

        return new SolarAssistantMqttMessage
        {
            Topic = Encoding.UTF8.GetString(payload, 2, topicLength),
            Payload = Encoding.UTF8.GetString(payload, offset, payload.Length - offset),
            Retain = (flags & 0x01) != 0,
            Qos = qos,
            ReceivedAtUtc = DateTime.UtcNow,
        };
    }

    private static async Task<(byte PacketType, byte Flags, byte[] Payload)?> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
    {
        var first = await ReadByteOrNullAsync(stream, ct);
        if (first is null)
            return null;

        var remaining = 0;
        var multiplier = 1;
        byte encoded;
        do
        {
            encoded = await ReadByteAsync(stream, ct);
            remaining += (encoded & 127) * multiplier;
            multiplier *= 128;
        }
        while ((encoded & 128) != 0);

        var payload = new byte[remaining];
        await stream.ReadExactlyAsync(payload, ct);
        return ((byte)(first.Value >> 4), (byte)(first.Value & 0x0F), payload);
    }

    private static Task<(byte PacketType, byte Flags, byte[] Payload)?> ReadPacketWithTimeoutAsync(
        NetworkStream stream,
        Action onTimeout,
        string timeoutMessage,
        CancellationToken ct) => WithTimeoutAsync(
            ReadPacketAsync(stream, ct),
            TimeSpan.FromSeconds(OperationTimeoutSeconds),
            onTimeout,
            timeoutMessage,
            ct);

    private static async Task<T> WithTimeoutAsync<T>(
        Task<T> operation,
        TimeSpan timeout,
        Action onTimeout,
        string timeoutMessage,
        CancellationToken ct)
    {
        var completed = await Task.WhenAny(operation, Task.Delay(timeout, ct));
        if (completed == operation)
            return await operation;

        onTimeout();
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException(timeoutMessage);
    }

    private static async Task WithTimeoutAsync(
        Task operation,
        TimeSpan timeout,
        Action onTimeout,
        string timeoutMessage,
        CancellationToken ct)
    {
        var completed = await Task.WhenAny(operation, Task.Delay(timeout, ct));
        if (completed == operation)
        {
            await operation;
            return;
        }

        onTimeout();
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException(timeoutMessage);
    }

    private static async Task WritePacketAsync(
        NetworkStream stream,
        byte packetType,
        byte flags,
        IReadOnlyCollection<byte> variableHeader,
        IReadOnlyCollection<byte> payload,
        CancellationToken ct)
    {
        var packet = new List<byte>(1 + variableHeader.Count + payload.Count + 4)
        {
            (byte)((packetType << 4) | flags),
        };
        WriteRemainingLength(packet, variableHeader.Count + payload.Count);
        packet.AddRange(variableHeader);
        packet.AddRange(payload);
        await stream.WriteAsync(packet.ToArray(), ct);
    }

    private static async Task<byte?> ReadByteOrNullAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer, ct);
        return read == 0 ? null : buffer[0];
    }

    private static async Task<byte> ReadByteAsync(NetworkStream stream, CancellationToken ct) =>
        await ReadByteOrNullAsync(stream, ct) ?? throw new EndOfStreamException("MQTT stream ended unexpectedly.");

    private static void WriteString(List<byte> output, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt16(output, bytes.Length);
        output.AddRange(bytes);
    }

    private static void WriteUInt16(List<byte> output, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)value));
        output.AddRange(bytes);
    }

    private static void WriteRemainingLength(List<byte> output, int value)
    {
        do
        {
            var encoded = value % 128;
            value /= 128;
            if (value > 0)
                encoded |= 128;
            output.Add((byte)encoded);
        }
        while (value > 0);
    }
}

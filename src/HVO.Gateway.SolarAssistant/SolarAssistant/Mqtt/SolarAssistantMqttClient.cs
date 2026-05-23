using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using HVO.Gateway.SolarAssistant.Configuration;

namespace HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

/// <summary>Minimal MQTT 3.1.1 read-only client for SolarAssistant discovery/state subscriptions.</summary>
public sealed class SolarAssistantMqttClient
{
    private readonly SolarAssistantOptions _options;

    public SolarAssistantMqttClient(SolarAssistantOptions options)
    {
        _options = options;
    }

    public async Task RunAsync(
        IReadOnlyList<string> subscriptions,
        Func<SolarAssistantMqttMessage, CancellationToken, Task> onMessage,
        CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_options.Host, _options.MqttPort, ct);
        await using var stream = tcp.GetStream();

        await ConnectAsync(stream, ct);
        await SubscribeAsync(stream, subscriptions, ct);

        while (!ct.IsCancellationRequested)
        {
            var packet = await ReadPacketAsync(stream, ct);
            if (packet is null)
                return;

            var (packetType, flags, payload) = packet.Value;
            if (packetType != 3)
                continue;

            var message = ReadPublish(flags, payload);
            if (message is not null)
                await onMessage(message, ct);
        }
    }

    private async Task ConnectAsync(NetworkStream stream, CancellationToken ct)
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
        WriteUInt16(variableHeader, 30);

        var payload = new List<byte>();
        WriteString(payload, clientId);
        if (!string.IsNullOrWhiteSpace(_options.MqttUsername))
        {
            WriteString(payload, _options.MqttUsername);
            if (!string.IsNullOrEmpty(_options.MqttPassword))
                WriteString(payload, _options.MqttPassword);
        }

        await WritePacketAsync(stream, packetType: 1, flags: 0, variableHeader, payload, ct);

        var response = await ReadPacketAsync(stream, ct) ?? throw new InvalidOperationException("MQTT broker closed before CONNACK.");
        if (response.PacketType != 2 || response.Payload.Length < 2)
            throw new InvalidOperationException("MQTT broker returned an invalid CONNACK.");

        var returnCode = response.Payload[1];
        if (returnCode != 0)
            throw new InvalidOperationException($"MQTT CONNACK rejected connection with return code {returnCode}.");
    }

    private static async Task SubscribeAsync(NetworkStream stream, IReadOnlyList<string> subscriptions, CancellationToken ct)
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

        var response = await ReadPacketAsync(stream, ct) ?? throw new InvalidOperationException("MQTT broker closed before SUBACK.");
        if (response.PacketType != 9)
            throw new InvalidOperationException("MQTT broker returned an invalid SUBACK.");
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

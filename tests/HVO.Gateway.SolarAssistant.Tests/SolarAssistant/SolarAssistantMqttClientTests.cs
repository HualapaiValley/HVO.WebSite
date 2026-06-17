using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantMqttClientTests
{
    [TestMethod]
    public async Task RunAsync_ValidConnackSuback_InvokesOnSubscribed()
    {
        await using var broker = await FakeMqttBroker.StartAsync(async connection =>
        {
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(2, 0, [0, 0]);
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(9, 0, [0, 1, 0]);
        });
        var subscribed = false;
        var client = CreateClient(broker.Port);

        await client.RunAsync(["homeassistant/#"], (_, _) => Task.CompletedTask, () => subscribed = true, CancellationToken.None);

        subscribed.Should().BeTrue();
    }

    [TestMethod]
    public async Task RunAsync_RejectsNonZeroConnack()
    {
        await using var broker = await FakeMqttBroker.StartAsync(async connection =>
        {
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(2, 0, [0, 5]);
        });
        var client = CreateClient(broker.Port);

        var act = () => client.RunAsync(["homeassistant/#"], (_, _) => Task.CompletedTask, () => { }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("MQTT CONNACK rejected connection with return code 5.");
    }

    [TestMethod]
    public async Task RunAsync_RejectsInvalidSubackPacketIdOrReturnCode()
    {
        await using var badPacketIdBroker = await FakeMqttBroker.StartAsync(async connection =>
        {
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(2, 0, [0, 0]);
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(9, 0, [0, 2, 0]);
        });
        var badPacketIdClient = CreateClient(badPacketIdBroker.Port);

        var badPacketIdAct = () => badPacketIdClient.RunAsync(["homeassistant/#"], (_, _) => Task.CompletedTask, () => { }, CancellationToken.None);

        await badPacketIdAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("MQTT broker returned SUBACK for packet id 2.");

        await using var rejectedBroker = await FakeMqttBroker.StartAsync(async connection =>
        {
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(2, 0, [0, 0]);
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(9, 0, [0, 1, 0x80]);
        });
        var rejectedClient = CreateClient(rejectedBroker.Port);

        var rejectedAct = () => rejectedClient.RunAsync(["homeassistant/#"], (_, _) => Task.CompletedTask, () => { }, CancellationToken.None);

        await rejectedAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("MQTT broker rejected subscription 'homeassistant/#'.");
    }

    [TestMethod]
    public async Task RunAsync_ParsesQos0AndQos1PublishPayloads()
    {
        await using var broker = await FakeMqttBroker.StartAsync(async connection =>
        {
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(2, 0, [0, 0]);
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(9, 0, [0, 1, 0]);
            await connection.WritePublishAsync("solar_assistant/total/pv_power/state", "1234", qos: 0, packetId: 0, retain: true);
            await connection.WritePublishAsync("solar_assistant/total/load_power/state", "567", qos: 1, packetId: 7, retain: false);
        });
        var messages = new ConcurrentQueue<SolarAssistantMqttMessage>();
        var client = CreateClient(broker.Port);

        await client.RunAsync(["solar_assistant/#"], (message, _) =>
        {
            messages.Enqueue(message);
            return Task.CompletedTask;
        }, () => { }, CancellationToken.None);

        messages.Should().HaveCount(2);
        messages.ElementAt(0).Topic.Should().Be("solar_assistant/total/pv_power/state");
        messages.ElementAt(0).Payload.Should().Be("1234");
        messages.ElementAt(0).Qos.Should().Be(0);
        messages.ElementAt(0).Retain.Should().BeTrue();
        messages.ElementAt(1).Topic.Should().Be("solar_assistant/total/load_power/state");
        messages.ElementAt(1).Payload.Should().Be("567");
        messages.ElementAt(1).Qos.Should().Be(1);
        messages.ElementAt(1).Retain.Should().BeFalse();
    }

    [TestMethod]
    public async Task RunAsync_KeepaliveTimeoutRaisesFailure()
    {
        await using var broker = await FakeMqttBroker.StartAsync(async connection =>
        {
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(2, 0, [0, 0]);
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(9, 0, [0, 1, 0]);
            var ping = await connection.ReadPacketAsync();
            ping.PacketType.Should().Be(12);
            await connection.WriteRawAsync([0x30, 0x02, 0x00]);
        });
        var client = CreateClient(broker.Port);

        var act = () => client.RunAsync(["homeassistant/#"], (_, _) => Task.CompletedTask, () => { }, CancellationToken.None);

        await act.Should().ThrowAsync<EndOfStreamException>();
    }

    private static SolarAssistantMqttClient CreateClient(int port) => new(new SolarAssistantOptions
    {
        Host = IPAddress.Loopback.ToString(),
        MqttPort = port,
        MqttClientId = "test-client",
    });

    private sealed class FakeMqttBroker : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serverTask;

        private FakeMqttBroker(TcpListener listener, Func<Connection, Task> handler)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _serverTask = RunAsync(handler);
        }

        public int Port { get; }

        public static Task<FakeMqttBroker> StartAsync(Func<Connection, Task> handler)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeMqttBroker(listener, handler));
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serverTask;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException or EndOfStreamException)
            {
            }
        }

        private async Task RunAsync(Func<Connection, Task> handler)
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            await handler(new Connection(stream));
        }
    }

    private sealed class Connection
    {
        private readonly NetworkStream _stream;

        public Connection(NetworkStream stream)
        {
            _stream = stream;
        }

        public async Task<(byte PacketType, byte Flags, byte[] Payload)> ReadPacketAsync()
        {
            var first = await ReadByteAsync();
            var remaining = 0;
            var multiplier = 1;
            byte encoded;
            do
            {
                encoded = await ReadByteAsync();
                remaining += (encoded & 127) * multiplier;
                multiplier *= 128;
            }
            while ((encoded & 128) != 0);

            var payload = new byte[remaining];
            await _stream.ReadExactlyAsync(payload);
            return ((byte)(first >> 4), (byte)(first & 0x0F), payload);
        }

        public Task WritePacketAsync(byte packetType, byte flags, IReadOnlyCollection<byte> payload)
        {
            var packet = new List<byte> { (byte)((packetType << 4) | flags) };
            WriteRemainingLength(packet, payload.Count);
            packet.AddRange(payload);
            return _stream.WriteAsync(packet.ToArray()).AsTask();
        }

        public Task WriteRawAsync(byte[] bytes) => _stream.WriteAsync(bytes).AsTask();

        public Task WritePublishAsync(string topic, string payload, int qos, int packetId, bool retain)
        {
            var body = new List<byte>();
            WriteString(body, topic);
            if (qos > 0)
                WriteUInt16(body, packetId);
            body.AddRange(Encoding.UTF8.GetBytes(payload));

            var flags = (byte)((qos << 1) | (retain ? 1 : 0));
            return WritePacketAsync(3, flags, body);
        }

        private async Task<byte> ReadByteAsync()
        {
            var buffer = new byte[1];
            var read = await _stream.ReadAsync(buffer);
            if (read == 0)
                throw new EndOfStreamException();
            return buffer[0];
        }
    }

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

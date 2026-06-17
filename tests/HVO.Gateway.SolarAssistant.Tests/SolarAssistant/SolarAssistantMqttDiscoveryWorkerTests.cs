using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using HVO.Gateway.SolarAssistant.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantMqttDiscoveryWorkerTests
{
    [TestMethod]
    public async Task ExecuteAsync_DisabledWhenHostMissing()
    {
        var store = new SolarAssistantMqttInventoryStore();
        using var worker = CreateWorker(new SolarAssistantOptions { Host = string.Empty, EnableMqttDiscovery = true }, store);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.Snapshot.LastError is not null);

        store.Snapshot.ConnectionState.Should().Be("disabled");
        store.Snapshot.LastError.Should().Be("SolarAssistant host is not configured.");
    }

    [TestMethod]
    public async Task ExecuteAsync_DisabledWhenMqttDiscoveryDisabled()
    {
        var store = new SolarAssistantMqttInventoryStore();
        using var worker = CreateWorker(new SolarAssistantOptions { Host = "127.0.0.1", EnableMqttDiscovery = false }, store);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.Snapshot.LastError is not null);

        store.Snapshot.ConnectionState.Should().Be("disabled");
        store.Snapshot.LastError.Should().Be("MQTT discovery is disabled.");
    }

    [TestMethod]
    public async Task ExecuteAsync_AppliesIncomingMessagesToInventoryStore()
    {
        await using var broker = await FakeMqttBroker.StartAsync(async connection =>
        {
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(2, 0, [0, 0]);
            await connection.ReadPacketAsync();
            await connection.WritePacketAsync(9, 0, [0, 1, 0, 0]);
            await connection.WritePublishAsync(
                "homeassistant/sensor/total_pv_power/config",
                """
                {"name":"PV power","stat_t":"solar_assistant/total/pv_power/state","unit_of_meas":"W","dev":{"name":"Axpert Max","mf":"Voltronic"}}
                """,
                retain: true);
            await connection.WritePublishAsync("solar_assistant/total/pv_power/state", "1234", retain: true);
            while (true)
                await connection.ReadPacketAsync();
        });
        var store = new SolarAssistantMqttInventoryStore();
        using var worker = CreateWorker(new SolarAssistantOptions
        {
            Host = IPAddress.Loopback.ToString(),
            MqttPort = broker.Port,
            EnableMqttDiscovery = true,
            MqttReconnectDelaySeconds = 1,
        }, store);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.Snapshot.EntityCount == 1 && store.Snapshot.StateTopicCount == 1);
        await worker.StopAsync(CancellationToken.None);

        var snapshot = store.Snapshot;
        snapshot.ConnectionState.Should().Be("connected");
        snapshot.Entities.Single().Name.Should().Be("PV power");
        snapshot.Devices.Single().Name.Should().Be("Axpert Max");
        snapshot.States.Single().PayloadLength.Should().Be(4);
    }

    private static SolarAssistantMqttDiscoveryWorker CreateWorker(SolarAssistantOptions options, SolarAssistantMqttInventoryStore store) =>
        new(Options.Create(options), store, NullLogger<SolarAssistantMqttDiscoveryWorker>.Instance);

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(25, cts.Token);
        }
    }

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
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException or EndOfStreamException or TaskCanceledException)
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

        public async Task ReadPacketAsync()
        {
            var first = await ReadByteAsync();
            _ = first;
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
        }

        public Task WritePacketAsync(byte packetType, byte flags, IReadOnlyCollection<byte> payload)
        {
            var packet = new List<byte> { (byte)((packetType << 4) | flags) };
            WriteRemainingLength(packet, payload.Count);
            packet.AddRange(payload);
            return _stream.WriteAsync(packet.ToArray()).AsTask();
        }

        public Task WritePublishAsync(string topic, string payload, bool retain)
        {
            var body = new List<byte>();
            WriteString(body, topic);
            body.AddRange(Encoding.UTF8.GetBytes(payload));
            return WritePacketAsync(3, retain ? (byte)1 : (byte)0, body);
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

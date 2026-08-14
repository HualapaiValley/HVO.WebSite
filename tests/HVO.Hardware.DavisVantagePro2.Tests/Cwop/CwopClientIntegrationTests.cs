using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Cwop;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Tests.Cwop;

[TestClass]
public sealed class CwopClientIntegrationTests
{
    [TestMethod]
    public async Task SendAsync_VerifiedLogin_SendsLoginAndPacketWithCrLf()
    {
        await using var server = new FakeAprsServer([Verified]);
        var client = CreateClient(server.Port);

        var result = await client.SendAsync("user DW4515 pass 12345 vers HVO-Davis 1.0", "DW4515>APRS,TCPIP*:packet", CancellationToken.None);
        var exchange = await server.Exchanges.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        result.Should().Be(CwopSendResult.Success);
        exchange.Login.Should().Be("user DW4515 pass 12345 vers HVO-Davis 1.0");
        exchange.Packet.Should().Be("DW4515>APRS,TCPIP*:packet");
        exchange.LoginTerminator.Should().Be("\r\n");
        exchange.PacketTerminator.Should().Be("\r\n");
    }

    [TestMethod]
    public async Task SendAsync_PassMinusOneWithUnverifiedAcknowledgement_StillSendsPacket()
    {
        await using var server = new FakeAprsServer([Unverified]);
        var client = CreateClient(server.Port);

        var result = await client.SendAsync(
            "user DW4515 pass -1 vers HVO-Davis 1.0",
            "DW4515>APRS,TCPIP*:packet",
            CancellationToken.None);
        var exchange = await server.Exchanges.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        result.Should().Be(CwopSendResult.Success);
        exchange.Packet.Should().Be("DW4515>APRS,TCPIP*:packet");
        exchange.PacketTerminator.Should().Be("\r\n");
    }

    [TestMethod]
    public async Task SendAsync_RejectionThenReconnect_ReportsRejectionAndRecovers()
    {
        await using var server = new FakeAprsServer([Rejected, Verified]);
        var client = CreateClient(server.Port);

        var rejected = await client.SendAsync("login-one", "packet-one", CancellationToken.None);
        var accepted = await client.SendAsync("login-two", "packet-two", CancellationToken.None);
        var first = await server.Exchanges.Reader.ReadAsync();
        var second = await server.Exchanges.Reader.ReadAsync();

        rejected.Outcome.Should().Be(CwopOutcome.LoginRejected);
        first.Packet.Should().BeNull();
        accepted.Should().Be(CwopSendResult.Success);
        second.Packet.Should().Be("packet-two");
        server.ConnectionCount.Should().Be(2);
    }

    [TestMethod]
    public async Task SendAsync_MalformedLoginResponse_IsRejectedAsRetryableInvalidResponse()
    {
        await using var server = new FakeAprsServer(["# unexpected response\r\n"]);
        var client = CreateClient(server.Port);

        var result = await client.SendAsync("login", "packet", CancellationToken.None);
        var exchange = await server.Exchanges.Reader.ReadAsync();

        result.Outcome.Should().Be(CwopOutcome.InvalidResponse);
        result.Retryable.Should().BeTrue();
        exchange.Packet.Should().BeNull();
    }

    [TestMethod]
    public async Task SendAsync_ImmediateDisconnect_IsIsolatedAsTransportFailure()
    {
        await using var server = new FakeAprsServer([Verified], disconnectBeforeBanner: true);
        var client = CreateClient(server.Port);

        var result = await client.SendAsync("login", "packet", CancellationToken.None);

        result.Outcome.Should().Be(CwopOutcome.Transport);
        result.Retryable.Should().BeTrue();
    }

    [TestMethod]
    public async Task SendAsync_ServerDoesNotSendBanner_TimesOutWithoutHanging()
    {
        await using var server = new FakeAprsServer([Verified], suppressBanner: true);
        var client = CreateClient(server.Port, operationTimeoutSeconds: 1);

        var result = await client.SendAsync("login", "packet", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(3));

        result.Outcome.Should().Be(CwopOutcome.Timeout);
        result.Retryable.Should().BeTrue();
    }

    [TestMethod]
    public async Task SendAsync_CancellationDuringRead_IsPropagated()
    {
        await using var server = new FakeAprsServer([Verified], suppressBanner: true);
        var client = CreateClient(server.Port, operationTimeoutSeconds: 10);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var action = () => client.SendAsync("login", "packet", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static CwopClient CreateClient(int port, int operationTimeoutSeconds = 2) => new(
        Options.Create(new CwopOptions
        {
            Enabled = true,
            StationId = "DW4515",
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ConnectTimeoutSeconds = 2,
            OperationTimeoutSeconds = operationTimeoutSeconds,
        }),
        TimeProvider.System);

    private sealed class FakeAprsServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new();
        private readonly IReadOnlyList<string> loginResponses;
        private readonly bool suppressBanner;
        private readonly bool disconnectBeforeBanner;
        private readonly Task runTask;
        private int connectionCount;

        public FakeAprsServer(
            IReadOnlyList<string> loginResponses,
            bool suppressBanner = false,
            bool disconnectBeforeBanner = false)
        {
            this.loginResponses = loginResponses;
            this.suppressBanner = suppressBanner;
            this.disconnectBeforeBanner = disconnectBeforeBanner;
            listener.Start();
            runTask = RunAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public int ConnectionCount => Volatile.Read(ref connectionCount);
        public Channel<Exchange> Exchanges { get; } = Channel.CreateUnbounded<Exchange>();

        private async Task RunAsync()
        {
            try
            {
                for (var index = 0; index < loginResponses.Count; index++)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    Interlocked.Increment(ref connectionCount);
                    await using var stream = client.GetStream();
                    if (disconnectBeforeBanner)
                        continue;
                    if (suppressBanner)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
                        continue;
                    }

                    await stream.WriteAsync(Encoding.ASCII.GetBytes("# javAPRSSrvr test\r\n"), cancellation.Token);
                    var login = await ReadRawLineAsync(stream, cancellation.Token);
                    var response = loginResponses[index];
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellation.Token);
                    var shouldReadPacket = response == Verified
                        || (response == Unverified && login.Value.Contains(" pass -1 ", StringComparison.Ordinal));
                    RawLine? packet = shouldReadPacket ? await ReadRawLineAsync(stream, cancellation.Token) : null;
                    await Exchanges.Writer.WriteAsync(new(login.Value, login.Terminator, packet?.Value, packet?.Terminator), cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        private static async Task<RawLine> ReadRawLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1];
            while (await stream.ReadAsync(buffer, cancellationToken) > 0)
            {
                bytes.Add(buffer[0]);
                if (buffer[0] == '\n')
                {
                    var terminator = bytes.Count >= 2 && bytes[^2] == '\r' ? "\r\n" : "\n";
                    return new(Encoding.ASCII.GetString([.. bytes[..^terminator.Length]]), terminator);
                }
            }
            throw new IOException("Client disconnected before line termination.");
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            try { await runTask; } catch (SocketException) when (cancellation.IsCancellationRequested) { }
            cancellation.Dispose();
        }

        private sealed record RawLine(string Value, string Terminator);
        public sealed record Exchange(string Login, string LoginTerminator, string? Packet, string? PacketTerminator);
    }

    private const string Verified = "# logresp DW4515 verified, server test\r\n";
    private const string Unverified = "# logresp DW4515 unverified, server test\r\n";
    private const string Rejected = "# logresp DW4515 rejected, server test\r\n";
}

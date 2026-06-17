using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using HVO.Hardware.DavisVantagePro2.Protocol;
using HVO.Hardware.DavisVantagePro2.Station;
using HVO.Hardware.DavisVantagePro2.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Hardware.DavisVantagePro2.Tests.Telemetry;

[TestClass]
public sealed class VantageStationHealthCheckTests
{
    [TestMethod]
    public async Task CheckHealthAsync_ReturnsHealthyWhenStationConnected()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new DavisConsoleClient(
            IPAddress.Loopback.ToString(),
            port,
            TimeSpan.FromSeconds(1),
            NullLogger<DavisConsoleClient>.Instance);
        var acceptTask = listener.AcceptTcpClientAsync();
        await client.OpenAsync(CancellationToken.None);
        using var acceptedClient = await acceptTask;
        var station = new VantageStation(client, NullLogger<VantageStation>.Instance);
        var healthCheck = new VantageStationHealthCheck(station);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Davis station is connected.");
    }

    [TestMethod]
    public async Task CheckHealthAsync_ReturnsUnhealthyWhenStationDisconnected()
    {
        using var client = new DavisConsoleClient(
            IPAddress.Loopback.ToString(),
            1,
            TimeSpan.FromSeconds(1),
            NullLogger<DavisConsoleClient>.Instance);
        var station = new VantageStation(client, NullLogger<VantageStation>.Instance);
        var healthCheck = new VantageStationHealthCheck(station);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Davis station is not connected.");
    }
}

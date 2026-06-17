using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaMacAddressResolverTests
{
    [TestMethod]
    public async Task TryFindHostByMacAsync_UsesSuppliedPortInsteadOfDefaultPort()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        var resolver = CreateResolver(server.Port, "127.0.0.1/32");

        var host = await resolver.TryFindHostByMacAsync("AA:BB:CC:DD:EE:01", server.Port, CancellationToken.None);

        host.Should().Be("127.0.0.1");
    }

    [TestMethod]
    public async Task TryFindHostByMacAsync_DoesNotCacheMissWhenLaterEnabledScanFails()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        var resolver = CreateResolver(server.Port, "127.0.0.1/32", "not-a-cidr");

        var first = await resolver.TryFindHostByMacAsync("AA:BB:CC:DD:EE:99", server.Port, CancellationToken.None);
        var requestsAfterFirstLookup = server.RequestKeys.Count;

        var second = await resolver.TryFindHostByMacAsync("AA:BB:CC:DD:EE:99", server.Port, CancellationToken.None);

        first.Should().BeNull();
        second.Should().BeNull();
        server.RequestKeys.Count.Should().BeGreaterThan(requestsAfterFirstLookup);
    }

    private static KasaMacAddressResolver CreateResolver(int defaultPort, params string[] cidrs)
    {
        var client = new KasaLegacyClient(TimeSpan.FromSeconds(2));
        var systemInfoParser = new KasaSystemInfoParser();
        var energyParser = new KasaEnergyParser();
        var capabilityDetector = new KasaCapabilityDetector();
        var probe = new KasaReadOnlyProbe(client, systemInfoParser, energyParser, capabilityDetector);
        var options = new KasaGatewayOptions
        {
            DefaultPort = defaultPort,
            MaxScanHosts = 32,
            MaxScanConcurrency = 1,
            Networks = cidrs.Select((cidr, index) => new KasaNetworkConfig
            {
                Name = $"test-{index}",
                Cidr = cidr,
                DiscoveryEnabled = true
            }).ToList()
        };

        return new KasaMacAddressResolver(probe, Options.Create(options), NullLogger<KasaMacAddressResolver>.Instance);
    }
}

using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Tests.Fakes;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaDeviceLocatorTests
{
    [TestMethod]
    public async Task LocateAsync_ConfiguredHostMatches_ReturnsConfiguredHost()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:01"
        };

        var result = await CreateLocator().LocateAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Host.Should().Be("127.0.0.1");
    }

    [TestMethod]
    public async Task LocateAsync_ConfiguredHostWrongDeviceWithoutMacLookup_FailsClosed()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("hs200-sysinfo.json"));
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port
        };

        var result = await CreateLocator().LocateAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("deviceId");
    }

    [TestMethod]
    public async Task LocateAsync_ConfiguredHostWrongDeviceWithMacLookup_ValidatesRecoveredHost()
    {
        await using var rightServer = new FakeKasaLegacyServer();
        rightServer.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        var lookup = new StaticMacLookup("AA:BB:CC:DD:EE:01", "127.0.0.1");
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "127.0.0.2",
            Port = rightServer.Port,
            MacAddress = "AA:BB:CC:DD:EE:01"
        };

        var result = await CreateLocator(lookup).LocateAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.SystemInfo!.DeviceId.Should().Be("EP25_DEVICE_ID_SANITIZED");
    }

    private static KasaDeviceLocator CreateLocator(IKasaMacAddressLookup? lookup = null) =>
        new(
            new KasaLegacyClient(TimeSpan.FromSeconds(2)),
            new KasaSystemInfoParser(),
            new KasaIdentityValidator(),
            lookup);

    private sealed class StaticMacLookup(string macAddress, string host) : IKasaMacAddressLookup
    {
        public Task<string?> TryFindHostByMacAsync(string requestedMacAddress, CancellationToken cancellationToken) =>
            Task.FromResult(KasaJson.MacAddressesEqual(macAddress, requestedMacAddress) ? host : null);
    }
}

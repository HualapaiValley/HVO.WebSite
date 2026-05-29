using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Tests.Fakes;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaDevicePollerTests
{
    [TestMethod]
    public async Task PollReadOnlyAsync_ValidatesIdentityBeforeReturningSnapshot()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("ep25-emeter.json"));
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            SourceId = "tplink-kasa:EP25_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:01",
            ExpectedModel = "EP25(US)",
            Capabilities = [KasaCapability.EnergyRealtime]
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.IdentityValidated.Should().BeTrue();
        result.Snapshot.Model.Should().Be("EP25(US)");
        result.Snapshot.Energy.Should().NotBeNull();
        result.Snapshot.Outlets.Should().ContainSingle();
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_WrongDeviceAtConfiguredHost_FailsClosed()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("hs200-sysinfo.json"));
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Snapshot.Should().BeNull();
        result.FailureReason.Should().Contain("deviceId");
    }

    private static KasaDevicePoller CreatePoller(TimeSpan timeout) =>
        new(
            new KasaLegacyClient(timeout),
            new KasaSystemInfoParser(),
            new KasaEnergyParser(),
            new KasaCapabilityDetector(),
            new KasaIdentityValidator());
}

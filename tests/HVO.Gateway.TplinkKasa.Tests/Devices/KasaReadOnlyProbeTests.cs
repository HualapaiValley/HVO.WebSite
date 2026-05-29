using FluentAssertions;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Tests.Fakes;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaReadOnlyProbeTests
{
    [TestMethod]
    public async Task ProbeAsync_ReadsReadOnlyMetadataWithoutCommands()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("ep25-emeter.json"));
        server.RespondTo("schedule", "get_rules", FixtureLoader.Read("schedule-rules.json"));
        server.RespondTo("system", "get_led_off", FixtureLoader.Read("led-state.json"));
        var probe = new KasaReadOnlyProbe(
            new KasaLegacyClient(TimeSpan.FromSeconds(2)),
            new KasaSystemInfoParser(),
            new KasaEnergyParser(),
            new KasaCapabilityDetector());

        var result = await probe.ProbeAsync("127.0.0.1", server.Port, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.SystemInfo!.DeviceId.Should().Be("EP25_DEVICE_ID_SANITIZED");
        result.Energy.Should().NotBeNull();
        result.Metadata["schedule"].IsSupported.Should().BeTrue();
        result.Metadata["led"].IsSupported.Should().BeTrue();
        result.Profile!.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.LedRead);
    }
}

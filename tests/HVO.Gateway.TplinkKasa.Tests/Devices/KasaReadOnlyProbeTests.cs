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
        server.RespondTo("emeter", "get_daystat", "{\"emeter\":{\"get_daystat\":{\"err_code\":0,\"day_list\":[]}}}");
        server.RespondTo("emeter", "get_monthstat", "{\"emeter\":{\"get_monthstat\":{\"err_code\":0,\"month_list\":[]}}}");
        server.RespondTo("schedule", "get_rules", FixtureLoader.Read("schedule-rules.json"));
        server.RespondTo("schedule", "get_next_action", "{\"schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("system", "get_led_off", FixtureLoader.Read("led-state.json"));
        server.RespondTo("time", "get_time", "{\"time\":{\"get_time\":{\"err_code\":0,\"year\":2026,\"month\":5,\"mday\":29}}}");
        server.RespondTo("time", "get_timezone", "{\"time\":{\"get_timezone\":{\"err_code\":0,\"index\":42}}}");
        server.RespondTo("cnCloud", "get_info", "{\"cnCloud\":{\"get_info\":{\"err_code\":0,\"binded\":1}}}");
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
        result.Metadata["scheduleNextAction"].IsSupported.Should().BeTrue();
        result.Metadata["led"].IsSupported.Should().BeTrue();
        result.Metadata["time"].IsSupported.Should().BeTrue();
        result.Metadata["timezone"].IsSupported.Should().BeTrue();
        result.Metadata["cloud"].IsSupported.Should().BeTrue();
        result.SystemInfoShape.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.deviceId", "string"));
        result.Metadata["emeterDay"].Shape.Should().Contain(new KasaJsonFieldShape("emeter.get_daystat.day_list", "array(empty)"));
        result.Profile!.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.LedRead);
    }
}

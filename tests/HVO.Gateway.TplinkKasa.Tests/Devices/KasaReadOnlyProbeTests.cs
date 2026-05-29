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
        server.RespondTo("emeter", "get_vgain_igain", "{\"emeter\":{\"get_vgain_igain\":{\"err_code\":0,\"vgain\":123,\"igain\":456}}}");
        server.RespondTo("system", "get_dev_icon", "{\"system\":{\"get_dev_icon\":{\"err_code\":0,\"icon\":\"sanitized\",\"hash\":\"sanitized\"}}}");
        server.RespondTo("system", "get_download_state", "{\"system\":{\"get_download_state\":{\"err_code\":0,\"status\":0}}}");
        server.RespondTo("schedule", "get_rules", FixtureLoader.Read("schedule-rules.json"));
        server.RespondTo("schedule", "get_next_action", "{\"schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("system", "get_led_off", FixtureLoader.Read("led-state.json"));
        server.RespondTo("time", "get_time", "{\"time\":{\"get_time\":{\"err_code\":0,\"year\":2026,\"month\":5,\"mday\":29}}}");
        server.RespondTo("time", "get_timezone", "{\"time\":{\"get_timezone\":{\"err_code\":0,\"index\":42}}}");
        server.RespondTo("cnCloud", "get_info", "{\"cnCloud\":{\"get_info\":{\"err_code\":0,\"binded\":1}}}");
        server.RespondTo("cnCloud", "get_intl_fw_list", "{\"cnCloud\":{\"get_intl_fw_list\":{\"err_code\":0,\"fw_list\":[]}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_light_state", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_light_state\":{\"err_code\":0,\"on_off\":1,\"brightness\":50}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_light_details", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_light_details\":{\"err_code\":0,\"lamp_beam_angle\":220}}}");
        server.RespondTo("smartlife.iot.common.cloud", "get_info", "{\"smartlife.iot.common.cloud\":{\"get_info\":{\"err_code\":0,\"binded\":1}}}");
        server.RespondTo("smartlife.iot.common.timesetting", "get_time", "{\"smartlife.iot.common.timesetting\":{\"get_time\":{\"err_code\":0,\"year\":2026}}}");
        server.RespondTo("smartlife.iot.common.timesetting", "get_timezone", "{\"smartlife.iot.common.timesetting\":{\"get_timezone\":{\"err_code\":0,\"index\":42}}}");
        server.RespondTo("smartlife.iot.common.schedule", "get_rules", "{\"smartlife.iot.common.schedule\":{\"get_rules\":{\"err_code\":0,\"rule_list\":[]}}}");
        server.RespondTo("smartlife.iot.common.schedule", "get_next_action", "{\"smartlife.iot.common.schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("smartlife.iot.common.emeter", "get_realtime", "{\"smartlife.iot.common.emeter\":{\"get_realtime\":{\"err_code\":0,\"power_mw\":0}}}");
        server.RespondTo("smartlife.iot.dimmer", "get_default_behavior", "{\"smartlife.iot.dimmer\":{\"get_default_behavior\":{\"err_code\":0,\"double_click\":{\"mode\":\"none\"}}}}");
        server.RespondTo("smartlife.iot.dimmer", "get_dimmer_parameters", "{\"smartlife.iot.dimmer\":{\"get_dimmer_parameters\":{\"err_code\":0,\"fadeOnTime\":1000}}}");
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
        result.Metadata["cloudFirmware"].IsSupported.Should().BeTrue();
        result.Metadata["bulbLightState"].IsSupported.Should().BeTrue();
        result.Metadata["bulbLightDetails"].IsSupported.Should().BeTrue();
        result.Metadata["dimmerDefaultBehavior"].IsSupported.Should().BeTrue();
        result.Metadata["dimmerParameters"].IsSupported.Should().BeTrue();
        result.SystemInfoShape.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.deviceId", "string"));
        result.Metadata["emeterDay"].Shape.Should().Contain(new KasaJsonFieldShape("emeter.get_daystat.day_list", "array(empty)"));
        result.Metadata["bulbLightState"].Shape.Should().Contain(new KasaJsonFieldShape("smartlife.iot.smartbulb.lightingservice.get_light_state.brightness", "number"));
        result.Metadata["dimmerDefaultBehavior"].Shape.Should().Contain(new KasaJsonFieldShape("smartlife.iot.dimmer.get_default_behavior.double_click.mode", "string"));
        result.Profile!.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.LedRead);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.BulbLightRead);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.DimmerRead);
        result.Profile.Capabilities.Should().Contain(KasaCapability.LightState);
        result.Profile.Capabilities.Should().Contain(KasaCapability.Dimming);
        result.Metadata.Should().NotContainKey("wifiScan");
    }

    [TestMethod]
    public async Task ProbeAsync_ReadsPrivacySensitiveMetadataOnlyWhenRequested()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("ep25-emeter.json"));
        server.RespondTo("netif", "get_scaninfo", "{\"netif\":{\"get_scaninfo\":{\"err_code\":0,\"ap_list\":[{\"ssid\":\"private\",\"signal_level\":2}]}}}");
        var probe = new KasaReadOnlyProbe(
            new KasaLegacyClient(TimeSpan.FromSeconds(2)),
            new KasaSystemInfoParser(),
            new KasaEnergyParser(),
            new KasaCapabilityDetector());

        var result = await probe.ProbeAsync("127.0.0.1", server.Port, includePrivacySensitive: true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Metadata["wifiScan"].IsSupported.Should().BeTrue();
        result.Metadata["wifiScan"].Shape.Should().Contain(new KasaJsonFieldShape("netif.get_scaninfo.ap_list[].ssid", "string"));
        result.Profile!.MetadataCapabilities.Should().Contain(KasaMetadataCapability.WifiScanRead);
    }

    [TestMethod]
    public async Task ProbeAsync_UnsupportedMetadataDoesNotAddCapability()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        server.RespondTo("smartlife.iot.dimmer", "get_default_behavior", "{\"smartlife.iot.dimmer\":{\"get_default_behavior\":{\"err_code\":-1}}}");
        server.RespondTo("smartlife.iot.dimmer", "get_dimmer_parameters", "{\"smartlife.iot.dimmer\":{\"get_dimmer_parameters\":{\"err_code\":-1}}}");
        var probe = CreateProbe(TimeSpan.FromSeconds(2));

        var result = await probe.ProbeAsync("127.0.0.1", server.Port, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Metadata["dimmerDefaultBehavior"].IsSupported.Should().BeFalse();
        result.Metadata["dimmerParameters"].IsSupported.Should().BeFalse();
        result.Profile!.MetadataCapabilities.Should().NotContain(KasaMetadataCapability.DimmerRead);
        result.Profile.Capabilities.Should().NotContain(KasaCapability.Dimming);
    }

    [TestMethod]
    public async Task ScanCidrAsync_RejectsCidrWithTooManyHosts()
    {
        var probe = CreateProbe(TimeSpan.FromMilliseconds(10));
        var options = new KasaReadOnlyScanOptions
        {
            MaxHosts = 10,
            MaxConcurrency = 16,
            RequireConfiguredNetwork = false
        };

        var act = async () => await probe.ScanCidrAsync(
            "192.168.1.0/24",
            9999,
            4,
            options,
            includePrivacySensitive: false,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task ScanCidrAsync_RejectsConcurrencyAboveConfiguredLimit()
    {
        var probe = CreateProbe(TimeSpan.FromMilliseconds(10));
        var options = new KasaReadOnlyScanOptions
        {
            MaxHosts = 256,
            MaxConcurrency = 2,
            RequireConfiguredNetwork = false
        };

        var act = async () => await probe.ScanCidrAsync(
            "192.168.1.0/30",
            9999,
            3,
            options,
            includePrivacySensitive: false,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public async Task ScanCidrAsync_RequiresConfiguredNetworkByDefault()
    {
        var probe = CreateProbe(TimeSpan.FromMilliseconds(10));
        var options = new KasaReadOnlyScanOptions
        {
            MaxHosts = 256,
            MaxConcurrency = 16,
            RequireConfiguredNetwork = true,
            AllowedCidrs = ["192.168.2.0/24"]
        };

        var act = async () => await probe.ScanCidrAsync(
            "192.168.1.0/30",
            9999,
            2,
            options,
            includePrivacySensitive: false,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static KasaReadOnlyProbe CreateProbe(TimeSpan timeout) => new(
        new KasaLegacyClient(timeout),
        new KasaSystemInfoParser(),
        new KasaEnergyParser(),
        new KasaCapabilityDetector());
}

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
        server.RespondTo("time", "get_time", "{\"time\":{\"get_time\":{\"err_code\":0,\"year\":2026,\"month\":5,\"mday\":29}}}");
        server.RespondTo("time", "get_timezone", "{\"time\":{\"get_timezone\":{\"err_code\":0,\"index\":42}}}");
        server.RespondTo("cnCloud", "get_info", "{\"cnCloud\":{\"get_info\":{\"err_code\":0,\"binded\":1}}}");
        server.RespondTo("cnCloud", "get_intl_fw_list", "{\"cnCloud\":{\"get_intl_fw_list\":{\"err_code\":0,\"fw_list\":[]}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_light_state", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_light_state\":{\"err_code\":0,\"on_off\":1,\"brightness\":50}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_light_details", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_light_details\":{\"err_code\":0,\"lamp_beam_angle\":220}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_default_behavior", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_default_behavior\":{\"err_code\":0,\"soft_on\":{\"mode\":\"last_status\"}}}}");
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
        result.Metadata["bulbDefaultBehavior"].IsSupported.Should().BeTrue();
        result.Metadata["dimmerDefaultBehavior"].IsSupported.Should().BeTrue();
        result.Metadata["dimmerParameters"].IsSupported.Should().BeTrue();
        result.SystemInfoShape.Should().Contain(new KasaJsonFieldShape("system.get_sysinfo.deviceId", "string"));
        result.Metadata["emeterDay"].Shape.Should().Contain(new KasaJsonFieldShape("emeter.get_daystat.day_list", "array(empty)"));
        result.Metadata["bulbLightState"].Shape.Should().Contain(new KasaJsonFieldShape("smartlife.iot.smartbulb.lightingservice.get_light_state.brightness", "number"));
        result.Metadata["dimmerDefaultBehavior"].Shape.Should().Contain(new KasaJsonFieldShape("smartlife.iot.dimmer.get_default_behavior.double_click.mode", "string"));
        result.Metadata["led"].Shape.Should().Contain(new KasaJsonFieldShape("led_off", "number"));
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
    public async Task ProbeAsync_ReadsEp25EnergyHistoryAndScheduleMetadata()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("ep25-emeter.json"));
        server.RespondTo("emeter", "get_daystat", "{\"emeter\":{\"get_daystat\":{\"err_code\":0,\"day_list\":[{\"year\":2026,\"month\":6,\"day\":1,\"energy_wh\":3265}]}}}");
        server.RespondTo("emeter", "get_monthstat", "{\"emeter\":{\"get_monthstat\":{\"err_code\":0,\"month_list\":[{\"year\":2026,\"month\":6,\"energy_wh\":19775}]}}}");
        server.RespondTo("emeter", "get_vgain_igain", "{\"emeter\":{\"get_vgain_igain\":{\"err_code\":0,\"vgain\":124049,\"igain\":11231}}}");
        server.RespondTo("schedule", "get_rules", "{\"schedule\":{\"get_rules\":{\"err_code\":0,\"enable\":0,\"version\":2,\"rule_list\":[]}}}");
        server.RespondTo("schedule", "get_next_action", "{\"schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("count_down", "get_rules", "{\"count_down\":{\"get_rules\":{\"err_code\":0,\"rule_list\":[]}}}");
        server.RespondTo("anti_theft", "get_rules", "{\"anti_theft\":{\"get_rules\":{\"err_code\":0,\"enable\":0,\"version\":2,\"rule_list\":[]}}}");
        var probe = CreateProbe(TimeSpan.FromSeconds(2));

        var result = await probe.ProbeAsync("127.0.0.1", server.Port, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.SystemInfo!.Model.Should().Be("EP25(US)");
        result.Profile!.DeviceKind.Should().Be(KasaDeviceKind.Plug);
        result.Energy.Should().NotBeNull();
        result.Metadata["emeter"].IsSupported.Should().BeTrue();
        result.Metadata["emeterDay"].Shape.Should().Contain(new KasaJsonFieldShape("emeter.get_daystat.day_list[].energy_wh", "number"));
        result.Metadata["emeterMonth"].Shape.Should().Contain(new KasaJsonFieldShape("emeter.get_monthstat.month_list[].energy_wh", "number"));
        result.Metadata["emeterGain"].Shape.Should().Contain(new KasaJsonFieldShape("emeter.get_vgain_igain.vgain", "number"));
        result.Metadata["schedule"].IsSupported.Should().BeTrue();
        result.Metadata["scheduleNextAction"].IsSupported.Should().BeTrue();
        result.Metadata["countdown"].IsSupported.Should().BeTrue();
        result.Metadata["away"].IsSupported.Should().BeTrue();
        result.Profile.Capabilities.Should().Contain(KasaCapability.EnergyRealtime);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.EnergyTotal);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.CountdownRead);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.AwayModeRead);
    }

    [TestMethod]
    public async Task ProbeAsync_ReadsChildScopedMetadataForMultiOutletDevices()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("hs300-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("ep25-emeter.json"));
        server.RespondTo("emeter", "get_daystat", "{\"emeter\":{\"get_daystat\":{\"err_code\":0,\"day_list\":[]}}}");
        server.RespondTo("emeter", "get_monthstat", "{\"emeter\":{\"get_monthstat\":{\"err_code\":0,\"month_list\":[]}}}");
        server.RespondTo("schedule", "get_rules", FixtureLoader.Read("schedule-rules.json"));
        server.RespondTo("schedule", "get_next_action", "{\"schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("count_down", "get_rules", "{\"count_down\":{\"get_rules\":{\"err_code\":0,\"rule_list\":[]}}}");
        server.RespondTo("anti_theft", "get_rules", "{\"anti_theft\":{\"get_rules\":{\"err_code\":0,\"rule_list\":[]}}}");
        var probe = CreateProbe(TimeSpan.FromSeconds(2));

        var result = await probe.ProbeAsync("127.0.0.1", server.Port, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.SystemInfo!.Children.Should().HaveCount(6);
        result.Metadata["child1:emeter"].IsSupported.Should().BeTrue();
        result.Metadata["child1:emeterDay"].IsSupported.Should().BeTrue();
        result.Metadata["child1:schedule"].IsSupported.Should().BeTrue();
        result.Metadata["child1:scheduleNextAction"].IsSupported.Should().BeTrue();
        result.Metadata["child1:countdown"].IsSupported.Should().BeTrue();
        result.Metadata["child1:away"].IsSupported.Should().BeTrue();
        result.Metadata["child6:emeter"].IsSupported.Should().BeTrue();
        result.Metadata["child6:schedule"].Capability.Should().Be(KasaMetadataCapability.ScheduleRead);
        result.Profile!.MetadataCapabilities.Should().Contain(KasaMetadataCapability.EnergyRealtime);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
    }

    [TestMethod]
    public async Task ProbeAsync_ReadsChildScopedSchedulesWithoutEnergyForDualOutletDevices()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("kp200-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", "{\"emeter\":{\"err_code\":-1,\"err_msg\":\"module not support\"}}");
        server.RespondTo("emeter", "get_daystat", "{\"emeter\":{\"err_code\":-1,\"err_msg\":\"module not support\"}}");
        server.RespondTo("emeter", "get_monthstat", "{\"emeter\":{\"err_code\":-1,\"err_msg\":\"module not support\"}}");
        server.RespondTo("schedule", "get_rules", FixtureLoader.Read("schedule-rules.json"));
        server.RespondTo("schedule", "get_next_action", "{\"schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("count_down", "get_rules", "{\"count_down\":{\"get_rules\":{\"err_code\":0,\"rule_list\":[]}}}");
        server.RespondTo("anti_theft", "get_rules", "{\"anti_theft\":{\"get_rules\":{\"err_code\":0,\"rule_list\":[]}}}");
        var probe = CreateProbe(TimeSpan.FromSeconds(2));

        var result = await probe.ProbeAsync("127.0.0.1", server.Port, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.SystemInfo!.Children.Should().HaveCount(2);
        result.Energy.Should().BeNull();
        result.Metadata["child1:emeter"].IsSupported.Should().BeFalse();
        result.Metadata["child1:schedule"].IsSupported.Should().BeTrue();
        result.Metadata["child1:scheduleNextAction"].IsSupported.Should().BeTrue();
        result.Metadata["child1:countdown"].IsSupported.Should().BeTrue();
        result.Metadata["child1:away"].IsSupported.Should().BeTrue();
        result.Metadata["child2:emeter"].IsSupported.Should().BeFalse();
        result.Metadata["child2:schedule"].IsSupported.Should().BeTrue();
        result.Profile!.MetadataCapabilities.Should().NotContain(KasaMetadataCapability.EnergyRealtime);
        result.Profile.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
        result.Profile.Capabilities.Should().Contain(KasaCapability.ChildOutlets);
        result.Profile.Capabilities.Should().NotContain(KasaCapability.EnergyRealtime);
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

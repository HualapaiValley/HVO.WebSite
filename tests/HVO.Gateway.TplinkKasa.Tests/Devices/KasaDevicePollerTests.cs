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
    public async Task PollStatusAsync_ReadsOnlyFastStatusCommands()
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

        var result = await poller.PollStatusAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.ReadMetadata.Should().BeNull();
        server.RequestKeys.Should().BeEquivalentTo(["system.get_sysinfo", "emeter.get_realtime"]);
    }

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
        result.Snapshot.Capabilities.Should().Contain(KasaCapability.EnergyRealtime);
        result.Snapshot.ReadMetadata.Should().NotBeNull();
        result.Snapshot.ReadMetadata!.Support.EnergyRealtime.Should().BeTrue();
        result.Snapshot.Outlets.Should().ContainSingle();
    }

    [TestMethod]
    public async Task PollStatusAsync_ExposesEveryNamedChildOutlet()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("kp200-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("emeter-unsupported.json"));
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "KP200_DEVICE_ID_SANITIZED",
            SourceId = "tplink-kasa:kp200",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:03",
            ExpectedModel = "KP200(US)",
            ExpectedChildCount = 2
        };

        var result = await poller.PollStatusAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.DeviceKind.Should().Be(KasaDeviceKind.DualOutlet);
        result.Snapshot.IsOn.Should().BeTrue();
        result.Snapshot.Outlets.Should().HaveCount(2);
        result.Snapshot.Outlets.Select(outlet => outlet.Alias).Should().Equal("Top", "Bottom");
        result.Snapshot.Outlets.Select(outlet => outlet.IsOn).Should().Equal(true, false);
    }

    [TestMethod]
    public async Task PollStatusAsync_ExposesChildOutletRealtimeEnergy()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("hs300-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("ep25-emeter.json"));
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "HS300_DEVICE_ID_SANITIZED",
            SourceId = "tplink-kasa:hs300",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:02",
            ExpectedModel = "HS300(US)",
            ExpectedChildCount = 6,
            Capabilities = [KasaCapability.EnergyRealtime]
        };

        var result = await poller.PollStatusAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.DeviceKind.Should().Be(KasaDeviceKind.PowerStrip);
        result.Snapshot.Outlets.Should().HaveCount(6);
        result.Snapshot.Outlets.Select(outlet => outlet.Energy?.PowerW).Should().OnlyContain(power => power == 4.2);
        server.RequestKeys.Count(key => key == "emeter.get_realtime").Should().Be(7);
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_ReadsRealtimeEnergyWhenCapabilityIsNotConfigured()
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
            ExpectedModel = "EP25(US)"
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.IsDegraded.Should().BeFalse();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Energy.Should().NotBeNull();
        result.Snapshot.Energy!.PowerW.Should().Be(4.2);
        result.Snapshot.Capabilities.Should().Contain(KasaCapability.EnergyRealtime);
        result.Snapshot.MetadataCapabilities.Should().Contain(KasaMetadataCapability.EnergyRealtime);
        result.Snapshot.ReadMetadata.Should().NotBeNull();
        result.Snapshot.ReadMetadata!.Support.EnergyRealtime.Should().BeTrue();
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_UnsupportedUnconfiguredEnergyDoesNotDegradeSnapshot()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("hs220-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("emeter-unsupported.json"));
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "HS220_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:06",
            ExpectedModel = "HS220(US)"
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.IsDegraded.Should().BeFalse();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Energy.Should().BeNull();
        result.Snapshot.ReadMetadata.Should().NotBeNull();
        result.Snapshot.ReadMetadata!.Support.EnergyRealtime.Should().BeFalse();
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_ExposesSupportedScheduleMetadata()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("hs220-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("emeter-unsupported.json"));
        server.RespondTo("schedule", "get_rules", FixtureLoader.Read("schedule-rules.json"));
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "HS220_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:06",
            ExpectedModel = "HS220(US)"
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.ReadMetadata.Should().NotBeNull();
        result.Snapshot.ReadMetadata!.Schedule.Should().NotBeNull();
        result.Snapshot.ReadMetadata.Schedule!.IsSupported.Should().BeTrue();
        result.Snapshot.ReadMetadata.Schedule.RuleCount.Should().Be(0);
        result.Snapshot.ReadMetadata.Support.ScheduleRules.Should().BeTrue();
        result.Snapshot.Capabilities.Should().Contain(KasaCapability.ScheduleMetadata);
        result.Snapshot.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_UsesBulbTimeMetadataWhenLegacyTimeModuleIsAbsent()
    {
        await using var server = new FakeKasaLegacyServer();
        var deviceLocalTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-7));
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("lb230-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("emeter-unsupported.json"));
        server.RespondTo("time", "get_time", "{\"time\":{\"get_time\":{\"err_code\":-1}}}");
        server.RespondTo("time", "get_timezone", "{\"time\":{\"get_timezone\":{\"err_code\":-1}}}");
        server.RespondTo("smartlife.iot.common.timesetting", "get_time", $"{{\"smartlife.iot.common.timesetting\":{{\"get_time\":{{\"err_code\":0,\"year\":{deviceLocalTime.Year},\"month\":{deviceLocalTime.Month},\"mday\":{deviceLocalTime.Day},\"hour\":{deviceLocalTime.Hour},\"min\":{deviceLocalTime.Minute},\"sec\":{deviceLocalTime.Second}}}}}}}");
        server.RespondTo("smartlife.iot.common.timesetting", "get_timezone", "{\"smartlife.iot.common.timesetting\":{\"get_timezone\":{\"err_code\":0,\"index\":7}}}");
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "LB230_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:08",
            ExpectedModel = "LB230(E26)"
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.DeviceInfo.Should().NotBeNull();
        result.Snapshot.DeviceInfo!.DeviceTime!.IsSupported.Should().BeTrue();
        result.Snapshot.DeviceInfo.Timezone!.Index.Should().Be(7);
        result.Snapshot.DeviceInfo.DeviceUtcOffsetMinutes.Should().BeInRange(-14 * 60, 14 * 60);
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_UsesBulbSpecificEnergyScheduleAndLightDetails()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", """
            {"system":{"get_sysinfo":{"err_code":0,"sw_ver":"1.6.0","hw_ver":"1.0","model":"LB230(E26)","deviceId":"LB230_DEVICE_ID_SANITIZED","alias":"Sanitized LB230","mic_mac":"AA:BB:CC:DD:EE:08","is_dimmable":1,"is_color":1,"is_variable_color_temp":1,"light_state":{"on_off":1,"hue":20,"saturation":70,"color_temp":2700,"brightness":10,"mode":"normal"},"preferred_state":[{"index":0,"brightness":15,"hue":20,"saturation":70,"color_temp":2700}]}}}
            """);
        server.RespondTo("emeter", "get_realtime", "{\"emeter\":{\"err_code\":-2001}}");
        server.RespondTo("smartlife.iot.common.emeter", "get_realtime", "{\"smartlife.iot.common.emeter\":{\"get_realtime\":{\"err_code\":0,\"power_mw\":7200}}}");
        server.RespondTo("schedule", "get_rules", "{\"schedule\":{\"err_code\":-2001}}");
        server.RespondTo("schedule", "get_next_action", "{\"schedule\":{\"err_code\":-2001}}");
        server.RespondTo("smartlife.iot.common.schedule", "get_rules", "{\"smartlife.iot.common.schedule\":{\"get_rules\":{\"err_code\":0,\"enable\":1,\"version\":2,\"rule_list\":[{}]}}}");
        server.RespondTo("smartlife.iot.common.schedule", "get_next_action", "{\"smartlife.iot.common.schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_light_details", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_light_details\":{\"err_code\":0,\"wattage\":11,\"max_lumens\":800,\"color_rendering_index\":80,\"incandescent_equivalent\":60,\"lamp_beam_angle\":220,\"min_voltage\":110,\"max_voltage\":120}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_default_behavior", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_default_behavior\":{\"err_code\":0,\"soft_on\":{\"mode\":\"last_status\"},\"hard_on\":{\"mode\":\"last_status\"}}}}");
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "LB230_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:08",
            ExpectedModel = "LB230(E26)"
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Energy!.PowerW.Should().Be(7.2);
        result.Snapshot.MetadataCapabilities.Should().Contain(KasaMetadataCapability.EnergyRealtime);
        result.Snapshot.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
        result.Snapshot.MetadataCapabilities.Should().Contain(KasaMetadataCapability.BulbLightRead);
        result.Snapshot.ReadMetadata!.Schedule!.RuleCount.Should().Be(1);
        result.Snapshot.ReadMetadata.Support.BulbLightDetails.Should().BeTrue();
        result.Snapshot.ReadMetadata.Support.BulbDefaultBehavior.Should().BeTrue();
        result.Snapshot.Light!.PreferredStates.Should().ContainSingle().Which.Brightness.Should().Be(15);
        result.Snapshot.Light.BulbDetails!.MaxLumens.Should().Be(800);
        result.Snapshot.Light.DefaultBehavior!.SoftOnMode.Should().Be("last_status");
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_UsesKl130BulbNamespacesAndPowerOnlyEnergy()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("kl130-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", "{\"emeter\":{\"err_code\":-2001,\"err_msg\":\"Module not support\"}}");
        server.RespondTo("smartlife.iot.common.emeter", "get_realtime", "{\"smartlife.iot.common.emeter\":{\"get_realtime\":{\"err_code\":0,\"power_mw\":10800}}}");
        server.RespondTo("schedule", "get_rules", "{\"schedule\":{\"err_code\":-2001,\"err_msg\":\"Module not support\"}}");
        server.RespondTo("schedule", "get_next_action", "{\"schedule\":{\"err_code\":-2001,\"err_msg\":\"Module not support\"}}");
        server.RespondTo("smartlife.iot.common.schedule", "get_rules", "{\"smartlife.iot.common.schedule\":{\"get_rules\":{\"err_code\":0,\"enable\":0,\"version\":2,\"rule_list\":[]}}}");
        server.RespondTo("smartlife.iot.common.schedule", "get_next_action", "{\"smartlife.iot.common.schedule\":{\"get_next_action\":{\"err_code\":0,\"type\":-1}}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_light_details", "{\"smartlife.iot.smartbulb.lightingservice\":{\"err_code\":-2000,\"err_msg\":\"Method not support\"}}");
        server.RespondTo("smartlife.iot.smartbulb.lightingservice", "get_default_behavior", "{\"smartlife.iot.smartbulb.lightingservice\":{\"get_default_behavior\":{\"err_code\":0,\"soft_on\":{\"mode\":\"last_status\"},\"hard_on\":{\"mode\":\"last_status\"}}}}");
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "KL130_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:07",
            ExpectedModel = "KL130(US)"
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.DeviceKind.Should().Be(KasaDeviceKind.Bulb);
        result.Snapshot.Energy.Should().NotBeNull();
        result.Snapshot.Energy!.PowerW.Should().Be(10.8);
        result.Snapshot.Energy.VoltageV.Should().BeNull();
        result.Snapshot.Energy.CurrentA.Should().BeNull();
        result.Snapshot.Energy.EnergyKWh.Should().BeNull();
        result.Snapshot.Light!.Brightness.Should().Be(75);
        result.Snapshot.Light.ColorTemperature.Should().Be(3500);
        result.Snapshot.Light.Mode.Should().Be("normal");
        result.Snapshot.Light.BulbDetails.Should().NotBeNull();
        result.Snapshot.Light.BulbDetails!.IsSupported.Should().BeFalse();
        result.Snapshot.Light.DefaultBehavior!.SoftOnMode.Should().Be("last_status");
        result.Snapshot.ReadMetadata!.Schedule!.RuleCount.Should().Be(0);
        result.Snapshot.ReadMetadata.Support.BulbDefaultBehavior.Should().BeTrue();
        result.Snapshot.ReadMetadata.Support.BulbLightDetails.Should().BeFalse();
        result.Snapshot.MetadataCapabilities.Should().Contain(KasaMetadataCapability.EnergyRealtime);
        result.Snapshot.MetadataCapabilities.Should().Contain(KasaMetadataCapability.ScheduleRead);
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

    [TestMethod]
    public async Task PollStatusAsync_LegacyScanAddedSyntheticDeviceIdValidatesByMac()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("kp200-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", FixtureLoader.Read("emeter-unsupported.json"));
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "kasa-aabbccddee03",
            SourceId = "tplink-kasa:kasa-aabbccddee03",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:03",
            ExpectedModel = "KP200(US)",
            ExpectedChildCount = 2
        };

        var result = await poller.PollStatusAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.IdentityValidated.Should().BeTrue();
        result.Snapshot.Outlets.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_SystemInfoTimeout_ReturnsFailedResult()
    {
        var poller = CreatePoller(TimeSpan.FromMilliseconds(25));
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = 9
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Snapshot.Should().BeNull();
        result.FailureReason.Should().Contain("Failed to read system info");
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_MalformedSystemInfo_ReturnsFailedResult()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", "{\"system\":{\"get_sysinfo\":[]}}");
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
        result.FailureReason.Should().Contain("Failed to read system info");
    }

    [TestMethod]
    public async Task PollReadOnlyAsync_EnergyReadFailure_ReturnsDegradedSnapshot()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("ep25-sysinfo.json"));
        server.RespondTo("emeter", "get_realtime", "{not-json");
        var poller = CreatePoller(TimeSpan.FromSeconds(2));
        var config = new KasaDeviceConfig
        {
            DeviceId = "EP25_DEVICE_ID_SANITIZED",
            Host = "127.0.0.1",
            Port = server.Port,
            MacAddress = "AA:BB:CC:DD:EE:01",
            ExpectedModel = "EP25(US)",
            Capabilities = [KasaCapability.EnergyRealtime]
        };

        var result = await poller.PollReadOnlyAsync(config, 9999, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.IsDegraded.Should().BeTrue();
        result.DegradedReason.Should().Contain("Failed to read realtime energy");
        result.DegradedReason.Should().Contain("Invalid JSON response");
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Energy.Should().BeNull();
    }

    private static KasaDevicePoller CreatePoller(TimeSpan timeout) =>
        new(
            new KasaLegacyClient(timeout),
            new KasaSystemInfoParser(),
            new KasaEnergyParser(),
            new KasaReadMetadataParser(),
            new KasaCapabilityDetector(),
            new KasaIdentityValidator());
}

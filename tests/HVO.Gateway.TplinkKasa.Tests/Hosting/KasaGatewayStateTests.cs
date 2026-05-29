using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Hosting;

[TestClass]
public sealed class KasaGatewayStateTests
{
    [TestMethod]
    public void GetInventory_DoesNotExposeRawDeviceIdOrHost()
    {
        var state = CreateState();

        var inventory = state.GetInventory();

        inventory.Devices.Should().ContainSingle();
        var device = inventory.Devices[0];
        device.DeviceIdConfigured.Should().BeTrue();
        device.HostConfigured.Should().BeTrue();
        device.SourceId.Should().Be("tplink-kasa:observatory-test");
        var text = System.Text.Json.JsonSerializer.Serialize(inventory);
        text.Should().NotContain("RAW_DEVICE_ID_SANITIZED");
        text.Should().NotContain("configured-device-host.example");
    }

    [TestMethod]
    public void GetInventory_IgnoresDisabledPlaceholderDevices()
    {
        var options = CreateConfig();
        options.Devices.Add(new KasaDeviceConfig
        {
            Enabled = false,
            DeviceId = string.Empty,
            SourceId = "tplink-kasa:disabled-placeholder",
            Host = string.Empty
        });
        var state = new KasaGatewayState(Options.Create(options));

        var inventory = state.GetInventory();
        var status = state.GetStatus();

        inventory.Devices.Should().ContainSingle();
        inventory.Devices.Should().NotContain(device => device.SourceId == "tplink-kasa:disabled-placeholder");
        status.ConfiguredDeviceCount.Should().Be(1);
    }

    [TestMethod]
    public void GetDevices_ReturnsAuthenticatedDeviceListShape()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)"), null));

        var devices = state.GetDevices();

        devices.GatewayId.Should().Be("test-gateway");
        devices.Devices.Should().ContainSingle();
        devices.Devices[0].SourceId.Should().Be("tplink-kasa:observatory-test");
    }

    [TestMethod]
    public void GetDeviceBySourceId_MatchesCaseInsensitively()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)"), null));

        var device = state.GetDeviceBySourceId("TPLINK-KASA:OBSERVATORY-TEST");

        device.Should().NotBeNull();
        device!.Model.Should().Be("EP25(US)");
    }

    [TestMethod]
    public void SearchDevices_FiltersByTextKindCapabilityAndState()
    {
        var options = CreateConfig();
        options.Devices.Add(new KasaDeviceConfig
        {
            Enabled = true,
            DeviceId = "SECOND_DEVICE_ID_SANITIZED",
            SourceId = "tplink-kasa:kitchen-bulb",
            Host = "second-host.example",
            ExpectedModel = "LB230(E26)",
            Capabilities = [KasaCapability.LightState]
        });
        var state = new KasaGatewayState(Options.Create(options));
        var plugConfig = options.Devices[0];
        var bulbConfig = options.Devices[1];
        state.ApplyResult(plugConfig, new KasaPollResult(CreateSnapshot(plugConfig, KasaDeviceKind.Plug, "EP25(US)", capabilities: new HashSet<KasaCapability> { KasaCapability.SwitchState, KasaCapability.EnergyRealtime }), null));
        state.ApplyResult(bulbConfig, new KasaPollResult(CreateSnapshot(bulbConfig, KasaDeviceKind.Bulb, "LB230(E26)", capabilities: new HashSet<KasaCapability> { KasaCapability.LightState }), null));

        var energyMatches = state.SearchDevices(new KasaDeviceSearchRequest("observatory", null, "plug", "EnergyRealtime", null, true, false));
        var bulbMatches = state.SearchDevices(new KasaDeviceSearchRequest(null, "LB230", "bulb", null, null, true, false));

        energyMatches.MatchCount.Should().Be(1);
        energyMatches.Devices[0].SourceId.Should().Be("tplink-kasa:observatory-test");
        bulbMatches.MatchCount.Should().Be(1);
        bulbMatches.Devices[0].SourceId.Should().Be("tplink-kasa:kitchen-bulb");
    }

    [TestMethod]
    public void GetStatus_DoesNotExposeRawDeviceIdHostMacAliasOrRawVendorJson()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        var snapshot = new KasaDeviceSnapshot(
            "RAW_DEVICE_ID_SANITIZED",
            config.EffectiveSourceId,
            "configured-device-host.example",
            DateTimeOffset.UtcNow,
            true,
            true,
            null,
            "Private Alias",
            "EP25(US)",
            "2.0",
            "1.0.0",
            "AA:BB:CC:DD:EE:01",
            KasaDeviceKind.Plug,
            new HashSet<KasaCapability> { KasaCapability.SwitchState },
            new HashSet<KasaMetadataCapability> { KasaMetadataCapability.Diagnostics },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5)],
            null,
            null,
            null,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var status = state.GetStatus();
        var text = System.Text.Json.JsonSerializer.Serialize(status);
        text.Should().NotContain("RAW_DEVICE_ID_SANITIZED");
        text.Should().NotContain("configured-device-host.example");
        text.Should().NotContain("AA:BB:CC:DD:EE:01");
        text.Should().NotContain("Private Alias");
        text.Should().NotContain("private-child-id");
        text.Should().NotContain("raw");
        status.Devices[0].SourceId.Should().Be("tplink-kasa:observatory-test");
        status.Devices[0].Outlets.Should().ContainSingle().Which.Index.Should().Be(1);
    }

    [TestMethod]
    public void GetStatus_ExposesReadMetadataWithoutRawVendorJson()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        var metadata = new KasaReadMetadataSnapshot(
            new KasaRuleMetadata(true, 0, null, true, 2, 3),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new KasaReadModuleSupport(false, true, false, false, false, false, false, false, false, false, false, false));
        var snapshot = new KasaDeviceSnapshot(
            "RAW_DEVICE_ID_SANITIZED",
            config.EffectiveSourceId,
            "configured-device-host.example",
            DateTimeOffset.UtcNow,
            true,
            true,
            null,
            "Private Alias",
            "EP25(US)",
            "2.0",
            "1.0.0",
            "AA:BB:CC:DD:EE:01",
            KasaDeviceKind.Plug,
            new HashSet<KasaCapability> { KasaCapability.SwitchState, KasaCapability.ScheduleMetadata },
            new HashSet<KasaMetadataCapability> { KasaMetadataCapability.ScheduleRead },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5)],
            null,
            null,
            metadata,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var status = state.GetStatus();

        status.Devices[0].ReadMetadata.Should().BeSameAs(metadata);
        status.Devices[0].ReadMetadata!.Schedule!.RuleCount.Should().Be(3);
        status.Devices[0].ReadMetadata!.Support.ScheduleRules.Should().BeTrue();
        var text = System.Text.Json.JsonSerializer.Serialize(status);
        text.Should().NotContain("raw");
        text.Should().NotContain("Private Alias");
    }

    [TestMethod]
    public void GetReviewStatus_ExposesSafeStatusWithoutRawIdentifiersOrRuntimeValues()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        var metadata = new KasaReadMetadataSnapshot(
            new KasaRuleMetadata(true, 0, null, true, 2, 3),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new KasaReadModuleSupport(true, true, false, false, false, false, false, false, false, false, false, false));
        var snapshot = new KasaDeviceSnapshot(
            "RAW_DEVICE_ID_SANITIZED",
            config.EffectiveSourceId,
            "configured-device-host.example",
            DateTimeOffset.UtcNow,
            true,
            true,
            null,
            "Private Alias",
            "EP25(US)",
            "2.0",
            "1.0.0",
            "AA:BB:CC:DD:EE:01",
            KasaDeviceKind.Plug,
            new HashSet<KasaCapability> { KasaCapability.SwitchState, KasaCapability.EnergyRealtime },
            new HashSet<KasaMetadataCapability> { KasaMetadataCapability.EnergyRealtime, KasaMetadataCapability.ScheduleRead },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5)],
            null,
            new KasaEnergyReading(42.5, 120.1, 0.35, 12.3, System.Text.Json.JsonDocument.Parse("{\"private\":\"energy\"}").RootElement.Clone()),
            metadata,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var reviewStatus = state.GetReviewStatus();
        var text = System.Text.Json.JsonSerializer.Serialize(reviewStatus);

        reviewStatus.Devices.Should().ContainSingle();
        var device = reviewStatus.Devices[0];
        device.Model.Should().Be("EP25(US)");
        device.DeviceKind.Should().Be("Plug");
        device.Capabilities.Should().Contain("EnergyRealtime");
        device.MetadataCapabilities.Should().Contain("ScheduleRead");
        device.IdentityValidated.Should().BeTrue();
        device.HasEnergyStatus.Should().BeTrue();
        device.ReadSupport!.EnergyRealtime.Should().BeTrue();
        text.Should().NotContain("RAW_DEVICE_ID_SANITIZED");
        text.Should().NotContain("tplink-kasa:observatory-test");
        text.Should().NotContain("configured-device-host.example");
        text.Should().NotContain("AA:BB:CC:DD:EE:01");
        text.Should().NotContain("Private Alias");
        text.Should().NotContain("private-child-id");
        text.Should().NotContain("42.5");
        text.Should().NotContain("raw");
        text.Should().NotContain("energy");
    }

    [TestMethod]
    public void GetReviewCurrentStatus_ExposesRuntimeValuesWithoutRawIdentifiersOrRawVendorJson()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        var metadata = new KasaReadMetadataSnapshot(
            new KasaRuleMetadata(true, 0, null, true, 2, 3),
            new KasaNextActionMetadata(true, 0, null, 1),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new KasaReadModuleSupport(true, true, true, false, false, false, false, false, false, false, false, false));
        var snapshot = new KasaDeviceSnapshot(
            "RAW_DEVICE_ID_SANITIZED",
            config.EffectiveSourceId,
            "configured-device-host.example",
            DateTimeOffset.UtcNow,
            true,
            true,
            null,
            "Private Alias",
            "EP25(US)",
            "2.0",
            "1.0.0",
            "AA:BB:CC:DD:EE:01",
            KasaDeviceKind.Plug,
            new HashSet<KasaCapability> { KasaCapability.SwitchState, KasaCapability.EnergyRealtime },
            new HashSet<KasaMetadataCapability> { KasaMetadataCapability.EnergyRealtime, KasaMetadataCapability.ScheduleRead },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5)],
            null,
            new KasaEnergyReading(42.5, 120.1, 0.35, 12.3, System.Text.Json.JsonDocument.Parse("{\"private\":\"energy\"}").RootElement.Clone()),
            metadata,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var currentStatus = state.GetReviewCurrentStatus();
        var text = System.Text.Json.JsonSerializer.Serialize(currentStatus);

        currentStatus.Devices.Should().ContainSingle();
        var device = currentStatus.Devices[0];
        device.Model.Should().Be("EP25(US)");
        device.DeviceKind.Should().Be("Plug");
        device.IsOn.Should().BeTrue();
        device.Outlets.Should().ContainSingle().Which.IsOn.Should().BeTrue();
        device.Energy!.PowerW.Should().Be(42.5);
        device.ReadMetadata!.Schedule!.RuleCount.Should().Be(3);
        text.Should().NotContain("RAW_DEVICE_ID_SANITIZED");
        text.Should().NotContain("tplink-kasa:observatory-test");
        text.Should().NotContain("configured-device-host.example");
        text.Should().NotContain("AA:BB:CC:DD:EE:01");
        text.Should().NotContain("Private Alias");
        text.Should().NotContain("private-child-id");
        text.Should().NotContain("raw");
        text.Should().NotContain("energy\"");
    }

    private static KasaGatewayState CreateState() => new(Options.Create(CreateConfig()));

    private static KasaDeviceSnapshot CreateSnapshot(
        KasaDeviceConfig config,
        KasaDeviceKind kind,
        string model,
        IReadOnlySet<KasaCapability>? capabilities = null) => new(
            config.DeviceId,
            config.EffectiveSourceId,
            config.Host,
            DateTimeOffset.UtcNow,
            true,
            true,
            null,
            "Private Alias",
            model,
            "2.0",
            "1.0.0",
            "AA:BB:CC:DD:EE:01",
            kind,
            capabilities ?? new HashSet<KasaCapability> { KasaCapability.SwitchState },
            new HashSet<KasaMetadataCapability> { KasaMetadataCapability.Diagnostics },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5)],
            null,
            null,
            null,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

    private static KasaGatewayOptions CreateConfig() => new()
    {
        GatewayId = "test-gateway",
        Devices =
        [
            new KasaDeviceConfig
            {
                Enabled = true,
                DeviceId = "RAW_DEVICE_ID_SANITIZED",
                SourceId = "tplink-kasa:observatory-test",
                Host = "configured-device-host.example",
                ExpectedModel = "EP25(US)",
                Capabilities = [KasaCapability.SwitchState]
            }
        ]
    };
}

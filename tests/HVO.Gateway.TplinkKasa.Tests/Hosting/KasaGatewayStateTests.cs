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

    private static KasaGatewayState CreateState() => new(Options.Create(CreateConfig()));

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

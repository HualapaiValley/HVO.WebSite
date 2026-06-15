using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Hosting;

[TestClass]
public sealed class KasaGatewayStateTests
{
    [TestMethod]
    public async Task GetInventoryAsync_DoesNotExposeRawDeviceIdOrHost()
    {
        var state = CreateState();

        var inventory = await state.GetInventoryAsync(CancellationToken.None);

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
    public async Task GetInventoryAsync_IgnoresDisabledPlaceholderDevices()
    {
        var options = CreateConfig();
        options.Devices.Add(new KasaDeviceConfig
        {
            Enabled = false,
            DeviceId = string.Empty,
            SourceId = "tplink-kasa:disabled-placeholder",
            Host = string.Empty
        });
        var state = CreateState(options);

        var inventory = await state.GetInventoryAsync(CancellationToken.None);
        var status = await state.GetStatusAsync(CancellationToken.None);

        inventory.Devices.Should().ContainSingle();
        inventory.Devices.Should().NotContain(device => device.SourceId == "tplink-kasa:disabled-placeholder");
        status.ConfiguredDeviceCount.Should().Be(1);
    }

    [TestMethod]
    public async Task GetDevicesAsync_ReturnsAuthenticatedDeviceListShape()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)"), null));

        var devices = await state.GetDevicesAsync(CancellationToken.None);

        devices.GatewayId.Should().Be("test-gateway");
        devices.Devices.Should().ContainSingle();
        devices.Devices[0].SourceId.Should().Be("tplink-kasa:observatory-test");
    }

    [TestMethod]
    public async Task GetStatusAsync_ExposesGatewayAndDeviceDisplayTimeZones()
    {
        var options = CreateConfig();
        options.DisplayTimeZoneId = "America/Phoenix";
        options.Devices[0].DisplayTimeZoneId = "America/New_York";
        var state = CreateState(options);

        var status = await state.GetStatusAsync(CancellationToken.None);

        status.DisplayTimeZoneId.Should().Be("America/Phoenix");
        status.Devices.Should().ContainSingle();
        status.Devices[0].DisplayTimeZoneId.Should().Be("America/New_York");
    }

    [TestMethod]
    public async Task GetDeviceBySourceIdAsync_MatchesCaseInsensitively()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)"), null));

        var device = await state.GetDeviceBySourceIdAsync("TPLINK-KASA:OBSERVATORY-TEST", CancellationToken.None);

        device.Should().NotBeNull();
        device!.Model.Should().Be("EP25(US)");
    }

    [TestMethod]
    public async Task SearchDevicesAsync_FiltersByTextKindCapabilityAndState()
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
        var state = CreateState(options);
        var plugConfig = options.Devices[0];
        var bulbConfig = options.Devices[1];
        state.ApplyResult(plugConfig, new KasaPollResult(CreateSnapshot(plugConfig, KasaDeviceKind.Plug, "EP25(US)", capabilities: new HashSet<KasaCapability> { KasaCapability.SwitchState, KasaCapability.EnergyRealtime }), null));
        state.ApplyResult(bulbConfig, new KasaPollResult(CreateSnapshot(bulbConfig, KasaDeviceKind.Bulb, "LB230(E26)", capabilities: new HashSet<KasaCapability> { KasaCapability.LightState }), null));

        var energyMatches = await state.SearchDevicesAsync(new KasaDeviceSearchRequest("observatory", null, "plug", "EnergyRealtime", null, true, false), CancellationToken.None);
        var bulbMatches = await state.SearchDevicesAsync(new KasaDeviceSearchRequest(null, "LB230", "bulb", null, null, true, false), CancellationToken.None);
        var vendorDetailMatches = await state.SearchDevicesAsync(new KasaDeviceSearchRequest("configured-device-host", null, null, null, null, true, false), CancellationToken.None);

        energyMatches.MatchCount.Should().Be(1);
        energyMatches.Devices[0].SourceId.Should().Be("tplink-kasa:observatory-test");
        bulbMatches.MatchCount.Should().Be(1);
        bulbMatches.Devices[0].SourceId.Should().Be("tplink-kasa:kitchen-bulb");
        vendorDetailMatches.MatchCount.Should().Be(1);
        vendorDetailMatches.Devices[0].DisplayName.Should().Be("Private Alias");
    }

    [TestMethod]
    public async Task GetStatusAsync_ExposesVendorVisibleDeviceDetailsWithoutRawConfiguredIdOrRawVendorJson()
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
            new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5, new KasaEnergyReading(12.3, 120.1, 0.2, 1.5, default))],
            null,
            null,
            CreateDeviceInfo("EP25(US)"),
            null,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var status = await state.GetStatusAsync(CancellationToken.None);
        var text = System.Text.Json.JsonSerializer.Serialize(status);
        text.Should().NotContain("RAW_DEVICE_ID_SANITIZED");
        text.Should().Contain("configured-device-host.example");
        text.Should().Contain("AA:BB:CC:DD:EE:01");
        text.Should().Contain("Private Alias");
        text.Should().NotContain("private-child-id");
        text.Should().NotContain("raw");
        status.Devices[0].SourceId.Should().Be("tplink-kasa:observatory-test");
        status.Devices[0].DisplayName.Should().Be("Private Alias");
        status.Devices[0].Host.Should().Be("configured-device-host.example");
        status.Devices[0].MacAddress.Should().Be("AA:BB:CC:DD:EE:01");
        status.Devices[0].DeviceInfo.Should().NotBeNull();
        var deviceInfo = status.Devices[0].DeviceInfo!;
        deviceInfo.Model.Should().Be("EP25(US)");
        deviceInfo.MacAddress.Should().Be("AA:BB:CC:DD:EE:01");
        status.Devices[0].Outlets.Should().ContainSingle().Which.Index.Should().Be(1);
        status.Devices[0].Outlets[0].DisplayName.Should().Be("Private Outlet");
        status.Devices[0].Outlets[0].Energy.Should().NotBeNull();
        status.Devices[0].Outlets[0].Energy!.PowerW.Should().Be(12.3);
    }

    [TestMethod]
    public async Task ApplyResult_FastStatusPollPreservesCachedFullDeviceMetadata()
    {
        var state = CreateState();
        var config = CreateConfig().Devices[0];
        var fullMetadata = new KasaReadMetadataSnapshot(
            null,
            null,
            null,
            null,
            new KasaDeviceTimeMetadata(true, 0, null, 2026, 6, 6, 11, 30, 0),
            new KasaTimezoneMetadata(true, 0, null, 7),
            null,
            new KasaCloudMetadata(true, 0, null, true, true, null, null, null, null),
            null,
            null,
            null,
            null,
            new KasaReadModuleSupport(false, false, false, false, false, true, true, false, true, false, false, false, false, false));
        var fullSnapshot = CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)") with
        {
            DeviceInfo = CreateDeviceInfo("EP25(US)") with
            {
                DeviceTime = fullMetadata.DeviceTime,
                Timezone = fullMetadata.Timezone,
                DeviceUtcOffsetMinutes = -420,
                DeviceTimeZoneLabel = "UTC-07:00",
                Cloud = fullMetadata.Cloud
            },
            ReadMetadata = fullMetadata
        };
        var fastSnapshot = CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)") with
        {
            DeviceInfo = CreateDeviceInfo("EP25(US)"),
            ReadMetadata = null
        };

        state.ApplyResult(config, new KasaPollResult(fullSnapshot, null));
        state.ApplyResult(config, new KasaPollResult(fastSnapshot, null));

        var status = await state.GetStatusAsync(CancellationToken.None);
        status.Devices[0].DeviceInfo!.Timezone!.Index.Should().Be(7);
        status.Devices[0].DeviceInfo!.DeviceUtcOffsetMinutes.Should().Be(-420);
        status.Devices[0].DeviceInfo!.Cloud!.IsConnected.Should().BeTrue();
        status.Devices[0].ReadMetadata.Should().BeSameAs(fullMetadata);
    }

    [TestMethod]
    public async Task GetStatusAsync_ExposesReadMetadataWithoutRawVendorJson()
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
            null,
            null,
            new KasaReadModuleSupport(false, true, false, false, false, false, false, false, false, false, false, false, false, false));
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
            new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5, null)],
            null,
            null,
            null,
            metadata,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var status = await state.GetStatusAsync(CancellationToken.None);

        status.Devices[0].ReadMetadata.Should().BeSameAs(metadata);
        status.Devices[0].ReadMetadata!.Schedule!.RuleCount.Should().Be(3);
        status.Devices[0].ReadMetadata!.Support.ScheduleRules.Should().BeTrue();
        var text = System.Text.Json.JsonSerializer.Serialize(status);
        text.Should().NotContain("raw");
        text.Should().Contain("Private Alias");
    }

    [TestMethod]
    public async Task GetReviewStatusAsync_ExposesSafeStatusWithoutRawIdentifiersOrRuntimeValues()
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
            null,
            null,
            new KasaReadModuleSupport(true, true, false, false, false, false, false, false, false, false, false, false, false, false));
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
            new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5, null)],
            null,
            new KasaEnergyReading(42.5, 120.1, 0.35, 12.3, System.Text.Json.JsonDocument.Parse("{\"private\":\"energy\"}").RootElement.Clone()),
            null,
            metadata,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var reviewStatus = await state.GetReviewStatusAsync(CancellationToken.None);
        var text = System.Text.Json.JsonSerializer.Serialize(reviewStatus);

        reviewStatus.Devices.Should().ContainSingle();
        var device = reviewStatus.Devices[0];
        device.Model.Should().Be("EP25(US)");
        device.DeviceKind.Should().Be("Plug");
        device.Capabilities.Should().Contain("EnergyRealtime");
        device.MetadataCapabilities.Should().Contain("ScheduleRead");
        device.CommandCapabilities.Should().Contain("SwitchPower");
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
    public async Task GetReviewCurrentStatusAsync_ExposesRuntimeValuesWithoutRawIdentifiersOrRawVendorJson()
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
            null,
            null,
            new KasaReadModuleSupport(true, true, true, false, false, false, false, false, false, false, false, false, false, false));
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
            new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5, null)],
            null,
            new KasaEnergyReading(42.5, 120.1, 0.35, 12.3, System.Text.Json.JsonDocument.Parse("{\"private\":\"energy\"}").RootElement.Clone()),
            null,
            metadata,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

        state.ApplyResult(config, new KasaPollResult(snapshot, null));

        var currentStatus = await state.GetReviewCurrentStatusAsync(CancellationToken.None);
        var text = System.Text.Json.JsonSerializer.Serialize(currentStatus);

        currentStatus.Devices.Should().ContainSingle();
        var device = currentStatus.Devices[0];
        device.Model.Should().Be("EP25(US)");
        device.DeviceKind.Should().Be("Plug");
        device.IsOn.Should().BeTrue();
        device.CommandCapabilities.Should().Contain("SwitchPower");
        var outlet = device.Outlets.Should().ContainSingle().Which;
        outlet.DisplayName.Should().Be("Private Outlet");
        outlet.IsOn.Should().BeTrue();
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

    [TestMethod]
    public async Task ApplyResult_PrefersConfiguredDisplayNameOverRuntimeAlias()
    {
        var options = CreateConfig();
        options.Devices[0].DisplayName = "Observatory Strip";
        var state = CreateState(options);
        var config = options.Devices[0];

        state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)"), null));

        var status = await state.GetStatusAsync(CancellationToken.None);

        status.Devices.Should().ContainSingle();
        status.Devices[0].DisplayName.Should().Be("Observatory Strip");
        status.Devices[0].Outlets[0].DisplayName.Should().Be("Private Outlet");
    }

    [TestMethod]
    public async Task GetStatusAsync_PropagatesGroupAndFavoriteMetadata()
    {
        var options = CreateConfig();
        options.Devices[0].GroupName = "Observatory";
        options.Devices[0].IsFavorite = true;
        var state = CreateState(options);
        var config = options.Devices[0];

        state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)"), null));

        var status = await state.GetStatusAsync(CancellationToken.None);
        var inventory = await state.GetInventoryAsync(CancellationToken.None);

        status.Devices[0].GroupName.Should().Be("Observatory");
        status.Devices[0].IsFavorite.Should().BeTrue();
        inventory.Devices[0].GroupName.Should().Be("Observatory");
        inventory.Devices[0].IsFavorite.Should().BeTrue();
    }

    [TestMethod]
    public async Task ApplyConfiguration_UpdatesCachedMetadataWithoutRepoll()
    {
        var options = CreateConfig();
        var state = CreateState(options);
        var config = options.Devices[0];

        state.ApplyResult(config, new KasaPollResult(CreateSnapshot(config, KasaDeviceKind.Plug, "EP25(US)"), null));

        config.DisplayName = "Pier Lights";
        config.GroupName = "Favorites";
        config.IsFavorite = true;
        state.ApplyConfiguration(config);

        var status = await state.GetStatusAsync(CancellationToken.None);

        status.Devices[0].DisplayName.Should().Be("Pier Lights");
        status.Devices[0].GroupName.Should().Be("Favorites");
        status.Devices[0].IsFavorite.Should().BeTrue();
        status.Devices[0].Outlets[0].DisplayName.Should().Be("Private Outlet");
    }

    private static KasaGatewayState CreateState() => CreateState(CreateConfig());

    private static KasaGatewayState CreateState(KasaGatewayOptions options)
    {
        var root = Path.Combine(Path.GetTempPath(), "hvo-kasa-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        options.DeviceRegistryPath = "kasa-devices.json";
        var registry = new KasaDeviceRegistry(Options.Create(options), new TestWebHostEnvironment(root));
        return new KasaGatewayState(Options.Create(options), registry);
    }

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
            new HashSet<KasaCommandCapability> { KasaCommandCapability.SwitchPower },
            true,
            [new KasaOutletSnapshot("private-child-id", 1, "Private Outlet", true, 5, null)],
            null,
            null,
            CreateDeviceInfo("EP25(US)"),
            null,
            System.Text.Json.JsonDocument.Parse("{\"private\":\"raw\"}").RootElement.Clone());

    private static KasaDeviceInfo CreateDeviceInfo(string model) => new(
        null,
        model,
        "2.0",
        "1.0.0",
        "AA:BB:CC:DD:EE:01",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

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

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "HVO.Gateway.TplinkKasa.Tests";

        public string WebRootPath { get; set; } = contentRootPath;

        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}

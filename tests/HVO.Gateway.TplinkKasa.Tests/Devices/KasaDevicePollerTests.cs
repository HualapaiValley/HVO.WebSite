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
        result.Snapshot.Capabilities.Should().Contain(KasaCapability.EnergyRealtime);
        result.Snapshot.ReadMetadata.Should().NotBeNull();
        result.Snapshot.ReadMetadata!.Support.EnergyRealtime.Should().BeTrue();
        result.Snapshot.Outlets.Should().ContainSingle();
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

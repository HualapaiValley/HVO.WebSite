using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Protocol;
using HVO.Gateway.TplinkKasa.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Devices;

[TestClass]
public sealed class KasaDeviceCommandServiceTests
{
    [TestMethod]
    public async Task SetPowerAsync_SingleRelay_SendsRelayStateCommand()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", Hs105SystemInfo);
        server.RespondTo("system", "set_relay_state", "{\"system\":{\"set_relay_state\":{\"err_code\":0}}}");
        var service = CreateService(server.Port);

        var result = await service.SetPowerAsync("tplink-kasa:test", false, null, CancellationToken.None);

        result.Success.Should().BeTrue();
        server.RequestKeys.Should().Contain("system.set_relay_state");
    }

    [TestMethod]
    public async Task SetPowerAsync_ChildOutlet_ResolvesChildByIndexServerSide()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", FixtureLoader.Read("kp200-sysinfo.json"));
        server.RespondTo("system", "set_relay_state", "{\"system\":{\"set_relay_state\":{\"err_code\":0}}}");
        var service = CreateService(server.Port, expectedModel: "KP200(US)", deviceId: "KP200_DEVICE_ID_SANITIZED", expectedChildCount: 2);

        var result = await service.SetPowerAsync("tplink-kasa:test", true, 2, CancellationToken.None);

        result.Success.Should().BeTrue();
        server.RequestKeys.Should().Contain("system.set_relay_state");
    }

    [TestMethod]
    public async Task SetAliasAsync_ValidAlias_SendsAliasCommand()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", Hs105SystemInfo);
        server.RespondTo("system", "set_dev_alias", "{\"system\":{\"set_dev_alias\":{\"err_code\":0}}}");
        var service = CreateService(server.Port);

        var result = await service.SetAliasAsync("tplink-kasa:test", "Desk Lamp", CancellationToken.None);

        result.Success.Should().BeTrue();
        server.RequestKeys.Should().Contain("system.set_dev_alias");
    }

    [TestMethod]
    public async Task SetDimmerBrightnessAsync_InvalidRange_DoesNotSendCommand()
    {
        await using var server = new FakeKasaLegacyServer();
        var service = CreateService(server.Port);

        var result = await service.SetDimmerBrightnessAsync("tplink-kasa:test", 0, CancellationToken.None);

        result.Success.Should().BeFalse();
        server.RequestKeys.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SetPowerAsync_IdentityMismatch_BlocksWrite()
    {
        await using var server = new FakeKasaLegacyServer();
        server.RespondTo("system", "get_sysinfo", Hs105SystemInfo);
        server.RespondTo("system", "set_relay_state", "{\"system\":{\"set_relay_state\":{\"err_code\":0}}}");
        var service = CreateService(server.Port, deviceId: "DIFFERENT_DEVICE_ID");

        var result = await service.SetPowerAsync("tplink-kasa:test", false, null, CancellationToken.None);

        result.Success.Should().BeFalse();
        server.RequestKeys.Should().NotContain("system.set_relay_state");
    }

    private static KasaDeviceCommandService CreateService(int port, string expectedModel = "HS105(US)", string deviceId = "HS105_DEVICE_ID_SANITIZED", int? expectedChildCount = null)
    {
        var options = new KasaGatewayOptions
        {
            DefaultPort = port,
            DeviceRegistryPath = $"kasa-command-service-{Guid.NewGuid():N}.json",
            Devices =
            [
                new KasaDeviceConfig
                {
                    Enabled = true,
                    DeviceId = deviceId,
                    SourceId = "tplink-kasa:test",
                    Host = "127.0.0.1",
                    Port = port,
                    ExpectedModel = expectedModel,
                    ExpectedChildCount = expectedChildCount,
                    DeviceKind = KasaDeviceKind.Auto
                }
            ]
        };
        var registry = new KasaDeviceRegistry(Options.Create(options), new TestWebHostEnvironment(Path.Combine(Path.GetTempPath(), "hvo-kasa-command-tests", Guid.NewGuid().ToString("N"))));
        var parser = new KasaSystemInfoParser();
        var identityValidator = new KasaIdentityValidator();
        var readOnlyProbe = new KasaReadOnlyProbe(new KasaLegacyClient(TimeSpan.FromSeconds(2)), parser, new KasaEnergyParser(), new KasaCapabilityDetector());
        var poller = new KasaDevicePoller(new KasaLegacyClient(TimeSpan.FromSeconds(2)), parser, new KasaEnergyParser(), new KasaReadMetadataParser(), new KasaCapabilityDetector(), identityValidator);
        var state = new HVO.Gateway.TplinkKasa.Hosting.KasaGatewayState(Options.Create(options), registry);
        var admin = new KasaAdminService(Options.Create(options), readOnlyProbe, registry, poller, state, null!, null!);
        return new KasaDeviceCommandService(new KasaLegacyLabClient(TimeSpan.FromSeconds(2)), registry, parser, identityValidator, admin, Options.Create(options), NullLogger<KasaDeviceCommandService>.Instance);
    }

    private const string Hs105SystemInfo = """
        {"system":{"get_sysinfo":{"err_code":0,"sw_ver":"1.5.6","hw_ver":"1.0","model":"HS105(US)","deviceId":"HS105_DEVICE_ID_SANITIZED","alias":"Sanitized HS105","mac":"AA:BB:CC:DD:EE:09","relay_state":1,"on_time":12}}}
        """;

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HVO.Gateway.TplinkKasa.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(EnsureRoot(contentRootPath));
        public string ContentRootPath { get; set; } = EnsureRoot(contentRootPath);
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = EnsureRoot(contentRootPath);
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(EnsureRoot(contentRootPath));

        private static string EnsureRoot(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

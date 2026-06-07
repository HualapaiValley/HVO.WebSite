using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Tests.Configuration;

[TestClass]
public sealed class KasaDeviceRegistryTests
{
    [TestMethod]
    public async Task GetDevicesAsync_SeedsRegistryFromOptionsWhenFileDoesNotExist()
    {
        var root = CreateRoot();
        var options = CreateOptions(root);
        options.Devices.Add(new KasaDeviceConfig
        {
            Enabled = true,
            DeviceId = "device-1",
            SourceId = "tplink-kasa:device-1",
            Host = "192.168.1.10",
            Capabilities = [KasaCapability.SwitchState]
        });
        options.Devices.Add(new KasaDeviceConfig
        {
            Enabled = false,
            DeviceId = "device-2",
            SourceId = "tplink-kasa:device-2",
            Host = "192.168.1.11"
        });

        var registry = CreateRegistry(options, root);

        var devices = await registry.GetDevicesAsync();
        var enabledDevices = await registry.GetEnabledDevicesAsync();

        devices.Should().HaveCount(2);
        enabledDevices.Should().ContainSingle().Which.SourceId.Should().Be("tplink-kasa:device-1");
        File.Exists(Path.Combine(root, "kasa-devices.json")).Should().BeTrue();
    }

    [TestMethod]
    public async Task AddOrUpdateAndRemoveAsync_PersistRegistryChanges()
    {
        var root = CreateRoot();
        var options = CreateOptions(root);
        var registry = CreateRegistry(options, root);

        await registry.AddOrUpdateAsync(new KasaDeviceConfig
        {
            Enabled = true,
            DeviceId = " device-1 ",
            SourceId = " tplink-kasa:device-1 ",
            Host = " 192.168.1.10 ",
            DeviceKind = KasaDeviceKind.Plug,
            Capabilities = [KasaCapability.SwitchState]
        });

        var reloaded = CreateRegistry(CreateOptions(root), root);
        var persisted = await reloaded.GetDevicesAsync();
        persisted.Should().ContainSingle().Which.Host.Should().Be("192.168.1.10");

        var removed = await reloaded.RemoveAsync("TPLINK-KASA:DEVICE-1");

        removed.Should().BeTrue();
        var afterRemove = CreateRegistry(CreateOptions(root), root);
        (await afterRemove.GetDevicesAsync()).Should().BeEmpty();
    }

    private static KasaGatewayOptions CreateOptions(string root) => new()
    {
        GatewayId = "test-gateway",
        DeviceRegistryPath = Path.Combine(root, "kasa-devices.json")
    };

    private static KasaDeviceRegistry CreateRegistry(KasaGatewayOptions options, string root) =>
        new(Options.Create(options), new TestWebHostEnvironment(root));

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hvo-kasa-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

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
using FluentAssertions;
using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace HVO.Gateway.TplinkKasa.Tests.Hosting;

[TestClass]
public sealed class KasaGatewayWorkerTests
{
    [TestMethod]
    public void CreateDeviceLoopSignature_ChangesWhenGroupOrFavoriteChanges()
    {
        var worker = new KasaGatewayWorker(
            Options.Create(new KasaGatewayOptions { DefaultPort = 9999, PollIntervalSeconds = 5 }),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<KasaGatewayWorker>.Instance);

        var baseConfig = new KasaDeviceConfig
        {
            Enabled = true,
            DeviceId = "device-id",
            SourceId = "tplink-kasa:test-device",
            DisplayName = "Desk Lamp",
            GroupName = "Office",
            IsFavorite = false,
            Host = "192.0.2.10",
            Port = 9999,
            DeviceKind = KasaDeviceKind.Plug,
            ProtocolFamily = KasaProtocolFamily.LegacyKasaTcp9999,
            SafetyClass = KasaSafetyClass.TelemetryOnly,
            Capabilities = [KasaCapability.SwitchState],
            MetadataCapabilities = [],
            CommandCapabilities = [KasaCommandCapability.SwitchPower],
            PollIntervalSeconds = 5
        };

        var groupChanged = Clone(baseConfig);
        groupChanged.GroupName = "Favorites";

        var favoriteChanged = Clone(baseConfig);
        favoriteChanged.IsFavorite = true;

        InvokeCreateDeviceLoopSignature(worker, baseConfig).Should().NotBe(InvokeCreateDeviceLoopSignature(worker, groupChanged));
        InvokeCreateDeviceLoopSignature(worker, baseConfig).Should().NotBe(InvokeCreateDeviceLoopSignature(worker, favoriteChanged));
    }

    private static string InvokeCreateDeviceLoopSignature(KasaGatewayWorker worker, KasaDeviceConfig config)
    {
        var method = typeof(KasaGatewayWorker).GetMethod("CreateDeviceLoopSignature", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (string)method!.Invoke(worker, [config])!;
    }

    private static KasaDeviceConfig Clone(KasaDeviceConfig source) => new()
    {
        Enabled = source.Enabled,
        DeviceId = source.DeviceId,
        SourceId = source.SourceId,
        DisplayName = source.DisplayName,
        GroupName = source.GroupName,
        IsFavorite = source.IsFavorite,
        Host = source.Host,
        Port = source.Port,
        MacAddress = source.MacAddress,
        NetworkName = source.NetworkName,
        DisplayTimeZoneId = source.DisplayTimeZoneId,
        ExpectedModel = source.ExpectedModel,
        ExpectedHardwareVersion = source.ExpectedHardwareVersion,
        ExpectedSoftwareVersion = source.ExpectedSoftwareVersion,
        ExpectedChildCount = source.ExpectedChildCount,
        ProtocolFamily = source.ProtocolFamily,
        DeviceKind = source.DeviceKind,
        Capabilities = [.. source.Capabilities],
        MetadataCapabilities = [.. source.MetadataCapabilities],
        CommandCapabilities = [.. source.CommandCapabilities],
        SafetyClass = source.SafetyClass,
        PollIntervalSeconds = source.PollIntervalSeconds
    };
}

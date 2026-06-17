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
            Options.Create(new KasaGatewayOptions.OutboxSection()),
            null!,
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

    [TestMethod]
    public void IsOutboxForwardingEnabled_ReturnsFalse_ForPlaceholderConfig()
    {
        var worker = CreateWorker(new KasaGatewayOptions.OutboxSection
        {
            ApiEndpoint = "https://example.test/api/v1/power/readings",
            ApiKey = "REPLACE_ME"
        });

        InvokeIsOutboxForwardingEnabled(worker).Should().BeFalse();
    }

    [TestMethod]
    public void IsOutboxForwardingEnabled_ReturnsTrue_ForConfiguredForwarding()
    {
        var worker = CreateWorker(new KasaGatewayOptions.OutboxSection
        {
            ApiEndpoint = "https://example.test/api/v1/power/readings",
            ApiKey = "secret"
        });

        InvokeIsOutboxForwardingEnabled(worker).Should().BeTrue();
    }

    private static KasaGatewayWorker CreateWorker(KasaGatewayOptions.OutboxSection outboxOptions) => new(
        Options.Create(new KasaGatewayOptions { DefaultPort = 9999, PollIntervalSeconds = 5 }),
        Options.Create(outboxOptions),
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        NullLogger<KasaGatewayWorker>.Instance);

    private static string InvokeCreateDeviceLoopSignature(KasaGatewayWorker worker, KasaDeviceConfig config)
    {
        var method = typeof(KasaGatewayWorker).GetMethod("CreateDeviceLoopSignature", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (string)method!.Invoke(worker, [config])!;
    }

    private static bool InvokeIsOutboxForwardingEnabled(KasaGatewayWorker worker)
    {
        var property = typeof(KasaGatewayWorker).GetProperty("IsOutboxForwardingEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        return (bool)property!.GetValue(worker)!;
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

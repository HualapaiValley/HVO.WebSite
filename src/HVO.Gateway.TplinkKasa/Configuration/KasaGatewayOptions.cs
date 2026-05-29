using HVO.Gateway.TplinkKasa.Devices;
using System.ComponentModel.DataAnnotations;

namespace HVO.Gateway.TplinkKasa.Configuration;

public sealed class KasaGatewayOptions
{
    public const string SectionName = "KasaGateway";

    public string GatewayId { get; set; } = "hvo-tplink-kasa";

    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int DefaultPort { get; set; } = 9999;

    [Range(1, 60)]
    public int SocketTimeoutSeconds { get; set; } = 3;

    [Range(5, 3600)]
    public int PollIntervalSeconds { get; set; } = 60;

    public bool RequireIdentityValidation { get; set; } = true;

    [Range(1, 4096)]
    public int MaxScanHosts { get; set; } = KasaReadOnlyScanOptions.DefaultMaxHosts;

    [Range(1, 256)]
    public int MaxScanConcurrency { get; set; } = KasaReadOnlyScanOptions.DefaultMaxConcurrency;

    public List<KasaNetworkConfig> Networks { get; set; } = [];

    public List<KasaDeviceConfig> Devices { get; set; } = [];
}

public sealed class KasaNetworkConfig
{
    public string Name { get; set; } = string.Empty;

    public string Cidr { get; set; } = string.Empty;

    public bool DiscoveryEnabled { get; set; }
}

public sealed class KasaDeviceConfig
{
    public bool Enabled { get; set; } = true;

    public string DeviceId { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int? Port { get; set; }

    public string? MacAddress { get; set; }

    public string? NetworkName { get; set; }

    public string? ExpectedModel { get; set; }

    public string? ExpectedHardwareVersion { get; set; }

    public string? ExpectedSoftwareVersion { get; set; }

    public int? ExpectedChildCount { get; set; }

    public KasaProtocolFamily ProtocolFamily { get; set; } = KasaProtocolFamily.LegacyKasaTcp9999;

    public KasaDeviceKind DeviceKind { get; set; } = KasaDeviceKind.Auto;

    public List<KasaCapability> Capabilities { get; set; } = [];

    public List<KasaMetadataCapability> MetadataCapabilities { get; set; } = [];

    public List<KasaCommandCapability> CommandCapabilities { get; set; } = [];

    public KasaSafetyClass SafetyClass { get; set; } = KasaSafetyClass.TelemetryOnly;

    public int? PollIntervalSeconds { get; set; }

    public string EffectiveSourceId => string.IsNullOrWhiteSpace(SourceId) ? $"tplink-kasa:{DeviceId}" : SourceId;

    public int EffectivePort(int defaultPort) => Port.GetValueOrDefault(defaultPort);
}

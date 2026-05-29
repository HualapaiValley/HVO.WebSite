using HVO.Gateway.TplinkKasa.Configuration;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaCapabilityDetector
{
    public KasaDeviceProfile Detect(KasaSystemInfo info, KasaDeviceConfig? config = null, KasaEnergyReading? energy = null)
    {
        var capabilities = new HashSet<KasaCapability>();
        var metadata = new HashSet<KasaMetadataCapability>
        {
            KasaMetadataCapability.FirmwareInfo,
            KasaMetadataCapability.Diagnostics
        };
        var commandCapabilities = new HashSet<KasaCommandCapability>();

        var kind = config?.DeviceKind is { } configuredKind && configuredKind != KasaDeviceKind.Auto
            ? configuredKind
            : InferKind(info);

        if (info.RelayState is not null)
        {
            capabilities.Add(KasaCapability.SwitchState);
            commandCapabilities.Add(KasaCommandCapability.SwitchPower);
        }

        if (info.Children.Count > 0)
        {
            capabilities.Add(KasaCapability.ChildOutlets);
            commandCapabilities.Add(KasaCommandCapability.SwitchPower);
        }

        if (info.LightState is not null)
        {
            capabilities.Add(KasaCapability.LightState);
            commandCapabilities.Add(KasaCommandCapability.SwitchPower);
        }

        if (info.LightState?.Brightness is not null || ModelContains(info, "HS220"))
        {
            capabilities.Add(KasaCapability.Dimming);
            commandCapabilities.Add(KasaCommandCapability.DimLevel);
        }

        if (info.LightState?.Hue is not null || info.LightState?.Saturation is not null || ModelContains(info, "KL130") || ModelContains(info, "LB230"))
        {
            capabilities.Add(KasaCapability.Color);
            commandCapabilities.Add(KasaCommandCapability.LightColor);
        }

        if (info.LightState?.ColorTemperature is not null || ModelContains(info, "KL130") || ModelContains(info, "LB230"))
        {
            capabilities.Add(KasaCapability.VariableColorTemperature);
            commandCapabilities.Add(KasaCommandCapability.LightColorTemperature);
        }

        if (energy is not null || config?.Capabilities.Contains(KasaCapability.EnergyRealtime) == true)
        {
            capabilities.Add(KasaCapability.EnergyRealtime);
            metadata.Add(KasaMetadataCapability.EnergyRealtime);
            if (energy?.EnergyKWh is not null)
            {
                metadata.Add(KasaMetadataCapability.EnergyTotal);
            }
        }

        capabilities.Add(KasaCapability.Diagnostics);

        if (config is not null)
        {
            capabilities.UnionWith(config.Capabilities);
            metadata.UnionWith(config.MetadataCapabilities);
            commandCapabilities.UnionWith(config.CommandCapabilities);
        }

        return new KasaDeviceProfile(kind, capabilities, metadata, commandCapabilities);
    }

    private static KasaDeviceKind InferKind(KasaSystemInfo info)
    {
        if (ModelContains(info, "HS300")) return KasaDeviceKind.PowerStrip;
        if (ModelContains(info, "KP200")) return KasaDeviceKind.DualOutlet;
        if (ModelContains(info, "HS210")) return KasaDeviceKind.ThreeWaySwitch;
        if (ModelContains(info, "HS220")) return KasaDeviceKind.Dimmer;
        if (ModelContains(info, "HS200")) return KasaDeviceKind.Switch;
        if (ModelContains(info, "KL") || ModelContains(info, "LB")) return KasaDeviceKind.Bulb;
        if (info.Children.Count > 1) return KasaDeviceKind.PowerStrip;
        if (info.Children.Count == 1) return KasaDeviceKind.DualOutlet;
        if (info.LightState is not null) return KasaDeviceKind.Bulb;
        if (info.RelayState is not null) return KasaDeviceKind.Plug;
        return KasaDeviceKind.Unknown;
    }

    private static bool ModelContains(KasaSystemInfo info, string value) =>
        info.Model?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
}

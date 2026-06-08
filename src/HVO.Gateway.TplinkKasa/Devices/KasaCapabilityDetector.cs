using HVO.Gateway.TplinkKasa.Configuration;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaCapabilityDetector
{
    public KasaDeviceProfile Detect(KasaSystemInfo info, KasaDeviceConfig? config = null, KasaEnergyReading? energy = null)
    {
        var definition = KasaDeviceProfileCatalog.Resolve(info);
        return definition.CreateRuntimeProfile(info, config, energy);
    }
}

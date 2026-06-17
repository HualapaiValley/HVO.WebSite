using HVO.Gateway.TplinkKasa.Devices;
using HVO.Gateway.TplinkKasa.Hosting;

namespace HVO.Gateway.TplinkKasa.Components;

public enum KasaOutletCommandMode
{
    RelayPower,
    LightState
}

public static class KasaOutletCommandFactory
{
    public static KasaLightCommandRequest BuildLightCommand(KasaDeviceStatus device, bool targetIsOn)
    {
        var light = device.Light;
        return new KasaLightCommandRequest(
            targetIsOn,
            Math.Clamp(light?.Brightness ?? 100, 1, 100),
            Math.Clamp(light?.Hue ?? 0, 0, 360),
            Math.Clamp(light?.Saturation ?? 0, 0, 100),
            Math.Clamp(light?.ColorTemperature ?? 0, 0, 9000));
    }
}

using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Protocol;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaDevicePoller(
    IKasaLegacyClient client,
    KasaSystemInfoParser systemInfoParser,
    KasaEnergyParser energyParser,
    KasaCapabilityDetector capabilityDetector,
    KasaIdentityValidator identityValidator)
{
    public async Task<KasaPollResult> PollReadOnlyAsync(KasaDeviceConfig config, int defaultPort, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            return new KasaPollResult(null, "Configured Host is required for direct polling.");
        }

        using var sysinfoResponse = await client.SendReadOnlyAsync(config.Host, config.EffectivePort(defaultPort), KasaCommands.GetSystemInfo, cancellationToken)
            .ConfigureAwait(false);
        var sysinfo = systemInfoParser.Parse(sysinfoResponse);
        var validation = identityValidator.Validate(config, sysinfo);
        if (!validation.IsValid)
        {
            return new KasaPollResult(null, validation.Reason);
        }

        KasaEnergyReading? energy = null;
        if (config.Capabilities.Contains(KasaCapability.EnergyRealtime))
        {
            using var energyResponse = await client.SendReadOnlyAsync(config.Host, config.EffectivePort(defaultPort), KasaCommands.GetRealtimeEnergy, cancellationToken)
                .ConfigureAwait(false);
            energy = energyParser.Parse(energyResponse);
        }

        var profile = capabilityDetector.Detect(sysinfo, config, energy);
        var snapshot = BuildSnapshot(config, sysinfo, profile, energy);
        return new KasaPollResult(snapshot, null);
    }

    private static KasaDeviceSnapshot BuildSnapshot(
        KasaDeviceConfig config,
        KasaSystemInfo info,
        KasaDeviceProfile profile,
        KasaEnergyReading? energy)
    {
        var outlets = new List<KasaOutletSnapshot>();
        if (info.Children.Count > 0)
        {
            for (var i = 0; i < info.Children.Count; i++)
            {
                var child = info.Children[i];
                outlets.Add(new KasaOutletSnapshot(
                    string.IsNullOrWhiteSpace(child.Id) ? $"outlet-{i + 1}" : child.Id,
                    i + 1,
                    child.Alias,
                    child.State is null ? null : child.State == 1,
                    child.OnTimeSeconds));
            }
        }
        else if (info.RelayState is not null)
        {
            outlets.Add(new KasaOutletSnapshot("outlet-1", 1, info.Alias, info.RelayState == 1, info.OnTimeSeconds));
        }

        var light = info.LightState is null
            ? null
            : new KasaLightSnapshot(
                info.LightState.IsOn is null ? null : info.LightState.IsOn == 1,
                info.LightState.Brightness,
                info.LightState.Hue,
                info.LightState.Saturation,
                info.LightState.ColorTemperature,
                info.LightState.Mode,
                info.LightState.RawLightState);

        var isOn = info.RelayState is null
            ? light?.IsOn
            : info.RelayState == 1;

        return new KasaDeviceSnapshot(
            config.DeviceId,
            config.EffectiveSourceId,
            config.Host,
            DateTimeOffset.UtcNow,
            true,
            true,
            null,
            info.Alias,
            info.Model,
            info.HardwareVersion,
            info.SoftwareVersion,
            KasaJson.NormalizeMacAddress(info.MacAddress),
            profile.DeviceKind,
            profile.Capabilities,
            profile.MetadataCapabilities,
            isOn,
            outlets,
            light,
            energy,
            info.RawSystemInfo);
    }
}

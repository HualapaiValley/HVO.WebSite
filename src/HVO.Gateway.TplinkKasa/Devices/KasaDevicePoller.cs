using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Protocol;
using System.Net.Sockets;
using System.Text.Json;

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

        KasaSystemInfo sysinfo;
        try
        {
            using var sysinfoResponse = await client.SendReadOnlyAsync(config.Host, config.EffectivePort(defaultPort), KasaCommands.GetSystemInfo, cancellationToken)
                .ConfigureAwait(false);
            sysinfo = systemInfoParser.Parse(sysinfoResponse);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return new KasaPollResult(null, $"Failed to read system info: {KasaFailureMessages.DescribeReadFailure(ex)}");
        }

        var validation = identityValidator.Validate(config, sysinfo);
        if (!validation.IsValid)
        {
            return new KasaPollResult(null, validation.Reason);
        }

        KasaEnergyReading? energy = null;
        if (config.Capabilities.Contains(KasaCapability.EnergyRealtime))
        {
            try
            {
                using var energyResponse = await client.SendReadOnlyAsync(config.Host, config.EffectivePort(defaultPort), KasaCommands.GetRealtimeEnergy, cancellationToken)
                    .ConfigureAwait(false);
                energy = energyParser.Parse(energyResponse);
            }
            catch (Exception ex) when (IsExpectedReadFailure(ex))
            {
                var degradedProfile = capabilityDetector.Detect(sysinfo, config, null);
                var degradedSnapshot = BuildSnapshot(config, sysinfo, degradedProfile, null);
                return new KasaPollResult(degradedSnapshot, null, $"Failed to read realtime energy: {KasaFailureMessages.DescribeReadFailure(ex)}");
            }
        }

        var profile = capabilityDetector.Detect(sysinfo, config, energy);
        var snapshot = BuildSnapshot(config, sysinfo, profile, energy);
        return new KasaPollResult(snapshot, null);
    }

    private static bool IsExpectedReadFailure(Exception ex) =>
        ex is IOException
            or TimeoutException
            or OperationCanceledException
            or InvalidDataException
            or JsonException
            or SocketException;

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

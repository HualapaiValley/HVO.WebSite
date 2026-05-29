using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Protocol;
using System.Net.Sockets;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaDevicePoller(
    IKasaLegacyClient client,
    KasaSystemInfoParser systemInfoParser,
    KasaEnergyParser energyParser,
    KasaReadMetadataParser metadataParser,
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
        string? degradedReason = null;
        try
        {
            using var energyResponse = await client.SendReadOnlyAsync(config.Host, config.EffectivePort(defaultPort), KasaCommands.GetRealtimeEnergy, cancellationToken)
                .ConfigureAwait(false);
            energy = energyParser.Parse(energyResponse);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            var failure = KasaFailureMessages.DescribeReadFailure(ex);
            if (config.Capabilities.Contains(KasaCapability.EnergyRealtime) || !IsUnsupportedEnergyResponse(ex))
            {
                degradedReason = $"Failed to read realtime energy: {failure}";
            }
        }

        var metadata = await ReadMetadataAsync(config, defaultPort, energy is not null, cancellationToken).ConfigureAwait(false);
        var profile = capabilityDetector.Detect(sysinfo, config, energy);
        profile = ApplyMetadataCapabilities(profile, metadata);
        var snapshot = BuildSnapshot(config, sysinfo, profile, energy, metadata);
        return new KasaPollResult(snapshot, null, degradedReason);
    }

    private async Task<KasaReadMetadataSnapshot> ReadMetadataAsync(KasaDeviceConfig config, int defaultPort, bool energyRealtimeSupported, CancellationToken cancellationToken)
    {
        var schedule = await ReadAsync(config, defaultPort, KasaCommands.GetScheduleRules, response => metadataParser.ParseRules(response, "schedule", "get_rules"), error => new KasaRuleMetadata(false, null, error, null, null, null), cancellationToken).ConfigureAwait(false);
        var scheduleNextAction = await ReadAsync(config, defaultPort, KasaCommands.GetNextScheduleAction, response => metadataParser.ParseNextAction(response, "schedule", "get_next_action"), error => new KasaNextActionMetadata(false, null, error, null), cancellationToken).ConfigureAwait(false);
        var countdown = await ReadAsync(config, defaultPort, KasaCommands.GetCountdownRules, response => metadataParser.ParseRules(response, "count_down", "get_rules"), error => new KasaRuleMetadata(false, null, error, null, null, null), cancellationToken).ConfigureAwait(false);
        var away = await ReadAsync(config, defaultPort, KasaCommands.GetAwayRules, response => metadataParser.ParseRules(response, "anti_theft", "get_rules"), error => new KasaRuleMetadata(false, null, error, null, null, null), cancellationToken).ConfigureAwait(false);
        var deviceTime = await ReadAsync(config, defaultPort, KasaCommands.GetTime, response => metadataParser.ParseTime(response, "time", "get_time"), error => new KasaDeviceTimeMetadata(false, null, error, null, null, null, null, null, null), cancellationToken).ConfigureAwait(false);
        var timezone = await ReadAsync(config, defaultPort, KasaCommands.GetTimezone, response => metadataParser.ParseTimezone(response, "time", "get_timezone"), error => new KasaTimezoneMetadata(false, null, error, null), cancellationToken).ConfigureAwait(false);
        var firmwareDownload = await ReadAsync(config, defaultPort, KasaCommands.GetDownloadState, metadataParser.ParseFirmwareDownload, error => new KasaFirmwareDownloadMetadata(false, null, error, null, null, null, null), cancellationToken).ConfigureAwait(false);
        var cloud = await ReadAsync(config, defaultPort, KasaCommands.GetCloudInfo, metadataParser.ParseCloud, error => new KasaCloudMetadata(false, null, error, null, null, null, null, null, null), cancellationToken).ConfigureAwait(false);
        var cloudFirmware = await ReadAsync(config, defaultPort, KasaCommands.GetCloudFirmwareList, metadataParser.ParseFirmwareList, error => new KasaFirmwareListMetadata(false, null, error, null), cancellationToken).ConfigureAwait(false);
        var dimmerDefault = await ReadAsync(config, defaultPort, KasaCommands.GetDimmerDefaultBehavior, metadataParser.ParseDimmerDefaultBehavior, error => new KasaDimmerDefaultBehaviorMetadata(false, null, error, null, null, null, null), cancellationToken).ConfigureAwait(false);
        var dimmerParameters = await ReadAsync(config, defaultPort, KasaCommands.GetDimmerParameters, metadataParser.ParseDimmerParameters, error => new KasaDimmerParameterMetadata(false, null, error, null, null, null, null, null, null, null), cancellationToken).ConfigureAwait(false);

        return new KasaReadMetadataSnapshot(
            schedule,
            scheduleNextAction,
            countdown,
            away,
            deviceTime,
            timezone,
            firmwareDownload,
            cloud,
            cloudFirmware,
            new KasaDimmerMetadata(dimmerDefault, dimmerParameters),
            new KasaReadModuleSupport(
                energyRealtimeSupported,
                schedule.IsSupported,
                scheduleNextAction.IsSupported,
                countdown.IsSupported,
                away.IsSupported,
                deviceTime.IsSupported,
                timezone.IsSupported,
                firmwareDownload.IsSupported,
                cloud.IsSupported,
                cloudFirmware.IsSupported,
                dimmerDefault.IsSupported,
                dimmerParameters.IsSupported));
    }

    private async Task<T> ReadAsync<T>(KasaDeviceConfig config, int defaultPort, string command, Func<JsonDocument, T> parse, Func<string, T> failed, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendReadOnlyAsync(config.Host, config.EffectivePort(defaultPort), command, cancellationToken)
                .ConfigureAwait(false);
            return parse(response);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return failed(KasaFailureMessages.DescribeReadFailure(ex));
        }
    }

    private static KasaDeviceProfile ApplyMetadataCapabilities(KasaDeviceProfile profile, KasaReadMetadataSnapshot metadata)
    {
        var capabilities = new HashSet<KasaCapability>(profile.Capabilities);
        var metadataCapabilities = new HashSet<KasaMetadataCapability>(profile.MetadataCapabilities);

        if (metadata.Schedule?.IsSupported == true || metadata.ScheduleNextAction?.IsSupported == true)
        {
            metadataCapabilities.Add(KasaMetadataCapability.ScheduleRead);
            capabilities.Add(KasaCapability.ScheduleMetadata);
        }

        if (metadata.Countdown?.IsSupported == true)
        {
            metadataCapabilities.Add(KasaMetadataCapability.CountdownRead);
            capabilities.Add(KasaCapability.ScheduleMetadata);
        }

        if (metadata.Away?.IsSupported == true)
        {
            metadataCapabilities.Add(KasaMetadataCapability.AwayModeRead);
            capabilities.Add(KasaCapability.ScheduleMetadata);
        }

        if (metadata.Dimmer?.DefaultBehavior?.IsSupported == true || metadata.Dimmer?.Parameters?.IsSupported == true)
        {
            metadataCapabilities.Add(KasaMetadataCapability.DimmerRead);
            capabilities.Add(KasaCapability.Dimming);
        }

        if (metadata.FirmwareDownload?.IsSupported == true || metadata.CloudFirmware?.IsSupported == true)
        {
            metadataCapabilities.Add(KasaMetadataCapability.FirmwareInfo);
        }

        if (metadata.DeviceTime?.IsSupported == true || metadata.Timezone?.IsSupported == true || metadata.Cloud?.IsSupported == true)
        {
            metadataCapabilities.Add(KasaMetadataCapability.Diagnostics);
        }

        return profile with { Capabilities = capabilities, MetadataCapabilities = metadataCapabilities };
    }

    private static bool IsExpectedReadFailure(Exception ex) =>
        ex is IOException
            or TimeoutException
            or OperationCanceledException
            or InvalidDataException
            or JsonException
            or SocketException;

    private static bool IsUnsupportedEnergyResponse(Exception ex) => ex is InvalidDataException;

    private static KasaDeviceSnapshot BuildSnapshot(
        KasaDeviceConfig config,
        KasaSystemInfo info,
        KasaDeviceProfile profile,
        KasaEnergyReading? energy,
        KasaReadMetadataSnapshot? metadata)
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
            metadata,
            info.RawSystemInfo);
    }
}

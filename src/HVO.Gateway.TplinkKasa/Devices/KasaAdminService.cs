using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaAdminService(
    IOptions<KasaGatewayOptions> options,
    KasaReadOnlyProbe probe,
    KasaDeviceRegistry registry,
    KasaDevicePoller poller,
    KasaGatewayState state,
    KasaGatewayWorker worker,
    ILogger<KasaAdminService> logger)
{
    public IReadOnlyList<KasaNetworkConfig> GetNetworks() => options.Value.Networks;

    public async Task<IReadOnlyList<KasaProbeResult>> ScanNetworkAsync(string networkName, CancellationToken cancellationToken)
    {
        var network = FindNetwork(networkName);
        var scanOptions = new KasaReadOnlyScanOptions
        {
            MaxHosts = options.Value.MaxScanHosts,
            MaxConcurrency = options.Value.MaxScanConcurrency,
            RequireConfiguredNetwork = true,
            AllowedCidrs = options.Value.Networks.Select(network => network.Cidr).Where(cidr => !string.IsNullOrWhiteSpace(cidr)).ToArray()
        };

        return await probe.ScanCidrAsync(
            network.Cidr,
            options.Value.DefaultPort,
            options.Value.MaxScanConcurrency,
            scanOptions,
            includePrivacySensitive: false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<KasaAdminOperationResult> AddByHostAsync(string host, string? networkName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return KasaAdminOperationResult.Failed("Host or IP address is required.");
        }

        var result = await probe.ProbeAsync(host.Trim(), options.Value.DefaultPort, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KasaAdminOperationResult.Failed(result.FailureReason ?? "Device probe failed.");
        }

        return await AddProbeResultAsync(result, networkName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KasaAdminOperationResult> AddByMacAsync(string macAddress, string networkName, CancellationToken cancellationToken)
    {
        var normalizedMac = KasaJson.NormalizeMacAddress(macAddress);
        if (string.IsNullOrWhiteSpace(normalizedMac))
        {
            return KasaAdminOperationResult.Failed("MAC address is required.");
        }

        var results = await ScanNetworkAsync(networkName, cancellationToken).ConfigureAwait(false);
        var match = results.FirstOrDefault(result => KasaJson.MacAddressesEqual(result.SystemInfo?.MacAddress, normalizedMac));
        if (match is null)
        {
            return KasaAdminOperationResult.Failed("No scanned device matched that MAC address.");
        }

        return await AddProbeResultAsync(match, networkName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KasaAdminOperationResult> AddProbeResultAsync(KasaProbeResult result, string? networkName, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess || result.SystemInfo is null || result.Profile is null)
        {
            return KasaAdminOperationResult.Failed(result.FailureReason ?? "Only successfully probed devices can be configured.");
        }

        var config = BuildDeviceConfig(result, networkName);
        var saved = await registry.AddOrUpdateAsync(config, cancellationToken).ConfigureAwait(false);
        await RefreshDeviceDetailsAsync(saved.EffectiveSourceId, cancellationToken).ConfigureAwait(false);
        return KasaAdminOperationResult.Succeeded($"Configured {saved.EffectiveSourceId}.", saved);
    }

    public async Task<KasaAdminOperationResult> RemoveAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return KasaAdminOperationResult.Failed("Source ID is required.");
        }

        var removed = await registry.RemoveAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return KasaAdminOperationResult.Failed("Configured device was not found.");
        }

        await TryPollOnceAsync(cancellationToken).ConfigureAwait(false);
        return KasaAdminOperationResult.Succeeded("Device removed.", null);
    }

    public async Task<KasaAdminOperationResult> RefreshDeviceDetailsAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return KasaAdminOperationResult.Failed("Source ID is required.");
        }

        var device = (await registry.GetEnabledDevicesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(device => string.Equals(device.EffectiveSourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return KasaAdminOperationResult.Failed("Configured device was not found.");
        }

        var result = await poller.PollReadOnlyAsync(device, options.Value.DefaultPort, cancellationToken).ConfigureAwait(false);
        state.ApplyResult(device, result);

        if (!result.IsSuccess)
        {
            return KasaAdminOperationResult.Failed(result.FailureReason ?? "Device detail refresh failed.");
        }

        return KasaAdminOperationResult.Succeeded($"Refreshed {device.EffectiveSourceId} details.", device);
    }

    public async Task<KasaAdminOperationResult> RefreshDeviceStatusAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return KasaAdminOperationResult.Failed("Source ID is required.");
        }

        var device = (await registry.GetEnabledDevicesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(device => string.Equals(device.EffectiveSourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return KasaAdminOperationResult.Failed("Configured device was not found.");
        }

        var result = await poller.PollStatusAsync(device, options.Value.DefaultPort, cancellationToken).ConfigureAwait(false);
        state.ApplyResult(device, result);

        if (!result.IsSuccess)
        {
            return KasaAdminOperationResult.Failed(result.FailureReason ?? "Device status refresh failed.");
        }

        return KasaAdminOperationResult.Succeeded($"Refreshed {device.EffectiveSourceId} status.", device);
    }

    private KasaNetworkConfig FindNetwork(string networkName)
    {
        var network = options.Value.Networks.FirstOrDefault(network => string.Equals(network.Name, networkName, StringComparison.OrdinalIgnoreCase));
        if (network is null || string.IsNullOrWhiteSpace(network.Cidr))
        {
            throw new InvalidOperationException("Select a configured network before scanning.");
        }

        return network;
    }

    private KasaDeviceConfig BuildDeviceConfig(KasaProbeResult result, string? networkName)
    {
        var sysinfo = result.SystemInfo!;
        var profile = result.Profile!;
        var normalizedMac = KasaJson.NormalizeMacAddress(sysinfo.MacAddress);
        var identity = !string.IsNullOrWhiteSpace(normalizedMac)
            ? normalizedMac.Replace(":", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant()
            : !string.IsNullOrWhiteSpace(sysinfo.DeviceId)
                ? sysinfo.DeviceId.Trim().ToLowerInvariant()
                : result.Host.Replace('.', '-');
        var publicDeviceId = $"kasa-{identity}";

        return new KasaDeviceConfig
        {
            Enabled = true,
            DeviceId = string.IsNullOrWhiteSpace(sysinfo.DeviceId) ? publicDeviceId : sysinfo.DeviceId.Trim(),
            SourceId = $"tplink-kasa:{publicDeviceId}",
            DisplayName = string.IsNullOrWhiteSpace(sysinfo.Alias) ? null : sysinfo.Alias.Trim(),
            Host = result.Host,
            Port = result.Port == options.Value.DefaultPort ? null : result.Port,
            MacAddress = normalizedMac,
            NetworkName = string.IsNullOrWhiteSpace(networkName) ? null : networkName,
            ExpectedModel = sysinfo.Model,
            ExpectedHardwareVersion = sysinfo.HardwareVersion,
            ExpectedSoftwareVersion = sysinfo.SoftwareVersion,
            ExpectedChildCount = sysinfo.Children.Count == 0 ? null : sysinfo.Children.Count,
            ProtocolFamily = KasaProtocolFamily.LegacyKasaTcp9999,
            DeviceKind = profile.DeviceKind,
            Capabilities = profile.Capabilities.Order().ToList(),
            MetadataCapabilities = profile.MetadataCapabilities.Order().ToList(),
            CommandCapabilities = profile.CommandCapabilities.Order().ToList(),
            SafetyClass = KasaSafetyClass.TelemetryOnly,
            PollIntervalSeconds = options.Value.PollIntervalSeconds
        };
    }

    private async Task TryPollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await worker.PollOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TP-Link/Kasa immediate refresh after admin change failed.");
        }
    }
}

public sealed record KasaAdminOperationResult(bool Success, string Message, KasaDeviceConfig? Device)
{
    public static KasaAdminOperationResult Succeeded(string message, KasaDeviceConfig? device) => new(true, message, device);

    public static KasaAdminOperationResult Failed(string message) => new(false, message, null);
}
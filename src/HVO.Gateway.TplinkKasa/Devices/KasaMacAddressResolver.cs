using HVO.Gateway.TplinkKasa.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaMacAddressResolver(
    KasaReadOnlyProbe probe,
    IOptions<KasaGatewayOptions> options,
    ILogger<KasaMacAddressResolver> logger) : IKasaMacAddressLookup
{
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, MacLookupResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> TryFindHostByMacAsync(string macAddress, CancellationToken cancellationToken)
    {
        var normalized = KasaJson.NormalizeMacAddress(macAddress);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (_cache.TryGetValue(normalized, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            logger.LogDebug("MAC lookup cache hit for {MacAddress}: {Host}", normalized, cached.Host ?? "(not found)");
            return cached.Host;
        }

        var completedAnyScan = false;

        foreach (var network in options.Value.Networks)
        {
            if (!network.DiscoveryEnabled || string.IsNullOrWhiteSpace(network.Cidr))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Scanning network {NetworkName} ({Cidr}) for MAC {MacAddress}", network.Name, network.Cidr, normalized);

            try
            {
                var scanOptions = new KasaReadOnlyScanOptions
                {
                    MaxHosts = options.Value.MaxScanHosts,
                    MaxConcurrency = options.Value.MaxScanConcurrency,
                    RequireConfiguredNetwork = false
                };

                var results = await probe.ScanCidrAsync(
                    network.Cidr,
                    options.Value.DefaultPort,
                    options.Value.MaxScanConcurrency,
                    scanOptions,
                    includePrivacySensitive: false,
                    cancellationToken).ConfigureAwait(false);

                completedAnyScan = true;

                foreach (var result in results)
                {
                    if (!result.IsSuccess || result.SystemInfo is null)
                    {
                        continue;
                    }

                    var scannedMac = KasaJson.NormalizeMacAddress(result.SystemInfo.MacAddress);
                    if (!string.IsNullOrWhiteSpace(scannedMac)
                        && KasaJson.MacAddressesEqual(scannedMac, normalized))
                    {
                        logger.LogInformation("Found MAC {MacAddress} at {Host} on network {NetworkName}", normalized, result.Host, network.Name);
                        _cache[normalized] = new MacLookupResult(result.Host, DateTimeOffset.UtcNow.Add(CacheExpiry));
                        return result.Host;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MAC scan on network {NetworkName} ({Cidr}) failed", network.Name, network.Cidr);
            }
        }

        logger.LogInformation("MAC {MacAddress} was not found on any configured network", normalized);
        if (completedAnyScan)
        {
            _cache[normalized] = new MacLookupResult(null, DateTimeOffset.UtcNow.Add(CacheExpiry));
        }

        return null;
    }

    private sealed record MacLookupResult(string? Host, DateTimeOffset ExpiresAt);
}

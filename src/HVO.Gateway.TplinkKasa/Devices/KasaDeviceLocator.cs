using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Protocol;

namespace HVO.Gateway.TplinkKasa.Devices;

public interface IKasaMacAddressLookup
{
    Task<string?> TryFindHostByMacAsync(string macAddress, CancellationToken cancellationToken);
}

public sealed class KasaDeviceLocator(
    IKasaLegacyClient client,
    KasaSystemInfoParser systemInfoParser,
    KasaIdentityValidator identityValidator,
    IKasaMacAddressLookup? macAddressLookup = null)
{
    public async Task<KasaDeviceLocationResult> LocateAsync(KasaDeviceConfig config, int defaultPort, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(config.Host))
        {
            var configuredHostResult = await TryValidateHostAsync(config, config.Host, defaultPort, cancellationToken).ConfigureAwait(false);
            if (configuredHostResult.IsSuccess)
            {
                return configuredHostResult;
            }

            if (string.IsNullOrWhiteSpace(config.MacAddress) || macAddressLookup is null)
            {
                return configuredHostResult;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.MacAddress) && macAddressLookup is not null)
        {
            var macHost = await macAddressLookup.TryFindHostByMacAsync(config.MacAddress, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(macHost)
                && !string.Equals(macHost, config.Host, StringComparison.OrdinalIgnoreCase))
            {
                return await TryValidateHostAsync(config, macHost, defaultPort, cancellationToken).ConfigureAwait(false);
            }
        }

        return KasaDeviceLocationResult.Failed("No configured host or MAC-assisted locator produced a validated device.");
    }

    private async Task<KasaDeviceLocationResult> TryValidateHostAsync(KasaDeviceConfig config, string host, int defaultPort, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendReadOnlyAsync(host, config.EffectivePort(defaultPort), KasaCommands.GetSystemInfo, cancellationToken)
                .ConfigureAwait(false);
            var info = systemInfoParser.Parse(response);
            var validation = identityValidator.Validate(config, info);
            return validation.IsValid
                ? KasaDeviceLocationResult.Success(host, info)
                : KasaDeviceLocationResult.Failed(validation.Reason ?? "Identity validation failed.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or TimeoutException or System.Net.Sockets.SocketException or System.Text.Json.JsonException)
        {
            return KasaDeviceLocationResult.Failed(ex.Message);
        }
    }
}

public sealed record KasaDeviceLocationResult(bool IsSuccess, string? Host, KasaSystemInfo? SystemInfo, string? FailureReason)
{
    public static KasaDeviceLocationResult Success(string host, KasaSystemInfo systemInfo) => new(true, host, systemInfo, null);

    public static KasaDeviceLocationResult Failed(string reason) => new(false, null, null, reason);
}

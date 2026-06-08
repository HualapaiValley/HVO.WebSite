using HVO.Gateway.TplinkKasa.Configuration;
using HVO.Gateway.TplinkKasa.Protocol;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public sealed class KasaDeviceCommandService(
    KasaLegacyLabClient client,
    KasaDeviceRegistry registry,
    KasaSystemInfoParser parser,
    KasaIdentityValidator identityValidator,
    KasaAdminService adminService,
    IOptions<KasaGatewayOptions> options,
    ILogger<KasaDeviceCommandService> logger)
{
    public async Task<KasaAdminOperationResult> SetAliasAsync(string sourceId, string alias, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(alias) || alias.Trim().Length > 64)
        {
            return KasaAdminOperationResult.Failed("Alias must be 1-64 characters.");
        }

        var device = await ReadValidatedDeviceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (!device.IsValid)
        {
            return device.Result!;
        }

        var sendFailure = await TrySendAsync(sourceId, device.Config!, BuildAliasCommand(alias.Trim()), cancellationToken).ConfigureAwait(false);
        if (sendFailure is not null)
        {
            return sendFailure;
        }

        StartReadback(sourceId);
        return KasaAdminOperationResult.Succeeded("Alias updated.", null);
    }

    public async Task<KasaAdminOperationResult> SetPowerAsync(string sourceId, bool isOn, int? outletIndex, CancellationToken cancellationToken)
    {
        var device = await ReadValidatedDeviceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (!device.IsValid)
        {
            return device.Result!;
        }

        var command = BuildPowerCommand(isOn);
        if (outletIndex is null)
        {
            var sendFailure = await TrySendAsync(sourceId, device.Config!, command, cancellationToken).ConfigureAwait(false);
            if (sendFailure is not null)
            {
                return sendFailure;
            }

            StartReadback(sourceId);
            return KasaAdminOperationResult.Succeeded("Power updated.", null);
        }

        var child = device.SystemInfo!.Children.ElementAtOrDefault(outletIndex.Value - 1);
        if (child?.Id is not { Length: > 0 } childId)
        {
            return KasaAdminOperationResult.Failed("The requested outlet was not present in the latest system information.");
        }

        var childSendFailure = await TrySendAsync(sourceId, device.Config!, KasaCommands.WithChildContext(childId, command), cancellationToken).ConfigureAwait(false);
        if (childSendFailure is not null)
        {
            return childSendFailure;
        }

        StartReadback(sourceId);
        return KasaAdminOperationResult.Succeeded($"Outlet {outletIndex.Value.ToString(CultureInfo.InvariantCulture)} power updated.", null);
    }

    public async Task<KasaAdminOperationResult> SetDimmerBrightnessAsync(string sourceId, int brightness, CancellationToken cancellationToken)
    {
        if (brightness is < 1 or > 100)
        {
            return KasaAdminOperationResult.Failed("Brightness must be between 1 and 100.");
        }

        var device = await ReadValidatedDeviceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (!device.IsValid)
        {
            return device.Result!;
        }

        var sendFailure = await TrySendAsync(sourceId, device.Config!, BuildDimmerBrightnessCommand(brightness), cancellationToken).ConfigureAwait(false);
        if (sendFailure is not null)
        {
            return sendFailure;
        }

        StartReadback(sourceId);
        return KasaAdminOperationResult.Succeeded("Dimmer brightness updated.", null);
    }

    public async Task<KasaAdminOperationResult> SetLightAsync(string sourceId, KasaLightCommandRequest request, CancellationToken cancellationToken)
    {
        if (request.Brightness is < 1 or > 100
            || request.Hue is < 0 or > 360
            || request.Saturation is < 0 or > 100
            || request.ColorTemperature is < 0 or > 9000)
        {
            return KasaAdminOperationResult.Failed("Light command values were outside the supported range.");
        }

        var device = await ReadValidatedDeviceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (!device.IsValid)
        {
            return device.Result!;
        }

        var sendFailure = await TrySendAsync(sourceId, device.Config!, BuildLightTransitionCommand(request), cancellationToken).ConfigureAwait(false);
        if (sendFailure is not null)
        {
            return sendFailure;
        }

        StartReadback(sourceId);
        return KasaAdminOperationResult.Succeeded("Light state updated.", null);
    }

    private async Task<KasaCommandDeviceRead> ReadValidatedDeviceAsync(string sourceId, CancellationToken cancellationToken)
    {
        var config = (await registry.GetEnabledDevicesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(device => string.Equals(device.EffectiveSourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (config is null)
        {
            return KasaCommandDeviceRead.Failed("Device is not configured or is disabled.");
        }

        if (string.IsNullOrWhiteSpace(config.Host))
        {
            return KasaCommandDeviceRead.Failed("Device host is not configured.");
        }

        try
        {
            using var response = await client.SendAsync(config.Host, config.EffectivePort(options.Value.DefaultPort), KasaCommands.GetSystemInfo, cancellationToken).ConfigureAwait(false);
            var info = parser.Parse(response);
            var validation = identityValidator.Validate(config, info);
            if (!validation.IsValid)
            {
                return KasaCommandDeviceRead.Failed(validation.Reason ?? "Device identity validation failed.");
            }

            return new KasaCommandDeviceRead(config, info, null);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or JsonException or OperationCanceledException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            return KasaCommandDeviceRead.Failed($"Device command validation failed: {KasaFailureMessages.DescribeReadFailure(ex)}");
        }
    }

    private async Task SendAsync(KasaDeviceConfig config, string command, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(config.Host, config.EffectivePort(options.Value.DefaultPort), command, cancellationToken).ConfigureAwait(false);
        if (TryFindErrorCode(response.RootElement, out var errCode) && errCode != 0)
        {
            throw new InvalidOperationException($"Kasa command returned err_code {errCode.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private async Task<KasaAdminOperationResult?> TrySendAsync(string sourceId, KasaDeviceConfig config, string command, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(config, command, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or JsonException or OperationCanceledException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            logger.LogWarning(ex, "TP-Link/Kasa command failed for {SourceId}.", sourceId);
            return KasaAdminOperationResult.Failed($"Device command failed: {KasaFailureMessages.DescribeReadFailure(ex)}");
        }
    }

    private void StartReadback(string sourceId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var refresh = await adminService.RefreshDeviceStatusAsync(sourceId, CancellationToken.None).ConfigureAwait(false);
                if (!refresh.Success)
                {
                    logger.LogWarning("TP-Link/Kasa command readback failed for {SourceId}: {Message}", sourceId, refresh.Message);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TP-Link/Kasa command readback failed for {SourceId}.", sourceId);
            }
        });
    }

    private static bool TryFindErrorCode(JsonElement element, out int errCode)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("err_code", out var errCodeElement) && KasaJson.TryGetInt(errCodeElement, out errCode))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindErrorCode(property.Value, out errCode))
                {
                    return true;
                }
            }
        }

        errCode = 0;
        return false;
    }

    private static string BuildAliasCommand(string alias) =>
        JsonSerializer.Serialize(new
        {
            system = new
            {
                set_dev_alias = new { alias }
            }
        });

    private static string BuildPowerCommand(bool isOn) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"system\":{{\"set_relay_state\":{{\"state\":{(isOn ? 1 : 0)}}}}}}}");

    private static string BuildDimmerBrightnessCommand(int brightness) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"smartlife.iot.dimmer\":{{\"set_brightness\":{{\"brightness\":{brightness}}}}}}}");

    private static string BuildLightTransitionCommand(KasaLightCommandRequest request) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["smartlife.iot.smartbulb.lightingservice"] = new Dictionary<string, object>
            {
                ["transition_light_state"] = new
                {
                    ignore_default = 1,
                    on_off = request.IsOn ? 1 : 0,
                    brightness = request.Brightness,
                    hue = request.Hue,
                    saturation = request.Saturation,
                    color_temp = request.ColorTemperature,
                    transition_period = 0
                }
            }
        });

    private sealed record KasaCommandDeviceRead(KasaDeviceConfig? Config, KasaSystemInfo? SystemInfo, KasaAdminOperationResult? Result)
    {
        public bool IsValid => Config is not null && SystemInfo is not null && Result is null;

        public static KasaCommandDeviceRead Failed(string message) => new(null, null, KasaAdminOperationResult.Failed(message));
    }
}

public sealed record KasaAliasCommandRequest(string Alias);

public sealed record KasaPowerCommandRequest(bool IsOn, int? OutletIndex);

public sealed record KasaDimmerCommandRequest(int Brightness);

public sealed record KasaLightCommandRequest(bool IsOn, int Brightness, int Hue, int Saturation, int ColorTemperature);
